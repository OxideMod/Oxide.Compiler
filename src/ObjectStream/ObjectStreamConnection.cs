using ObjectStream.IO;
using ObjectStream.Threading;
using Sentry;

namespace ObjectStream
{
    public class ObjectStreamConnection<TRead, TWrite>
        where TRead : class
        where TWrite : class
    {
        private readonly ObjectStreamWrapper<TRead, TWrite> _streamWrapper;
        private readonly Queue<TWrite> _writeQueue = new();
        private readonly AutoResetEvent _writeSignal = new(false);

        internal ObjectStreamConnection(Stream inStream, Stream outStream)
        {
            _streamWrapper = new ObjectStreamWrapper<TRead, TWrite>(inStream, outStream);
        }

        public event ConnectionMessageEventHandler<TRead, TWrite> ReceiveMessage;

        public event ConnectionExceptionEventHandler<TRead, TWrite> Error;

        public void Open()
        {
            Worker readWorker = new();
            readWorker.Error += OnError;
            readWorker.DoWork(ReadStream);

            Worker writeWorker = new();
            writeWorker.Error += OnError;
            writeWorker.DoWork(WriteStream);
        }

        public void PushMessage(TWrite message)
        {
            _writeQueue.Enqueue(message);
            _writeSignal.Set();
        }

        public void Close() => CloseImpl();

        private void CloseImpl()
        {
            Error = null;
            _streamWrapper.Close();
            _writeSignal.Set();
        }

        private void OnError(Exception exception)
        {
            if (Error != null)
            {
                Error(this, exception);
            }
        }

        private void ReadStream()
        {
            ITransaction transaction = SentrySdk.StartTransaction("Stream", "ReadMessages");
            while (_streamWrapper.CanRead)
            {
                ISpan child = transaction.StartChild("ReadMessage");
                TRead obj = _streamWrapper.ReadObject();
                ReceiveMessage?.Invoke(this, obj);
                child.SetExtra("ReadSuccess", obj != null);
                if (obj != null)
                {
                    child.Finish();
                    continue;
                }

                CloseImpl();
                child.Finish();
                break;
            }
            transaction.Finish();
        }

        private void WriteStream()
        {
            while (_streamWrapper.CanWrite)
            {
                _writeSignal.WaitOne();
                ITransaction transaction = SentrySdk.StartTransaction("Stream", "WriteObjectQueue");
                while (_writeQueue.Count > 0)
                {

                    _streamWrapper.WriteObject(_writeQueue.Dequeue());
                }
                transaction.Finish();
            }
        }
    }

    internal static class ConnectionFactory
    {
        public static ObjectStreamConnection<TRead, TWrite> CreateConnection<TRead, TWrite>(Stream inStream, Stream outStream)
            where TRead : class
            where TWrite : class
        {
            return new ObjectStreamConnection<TRead, TWrite>(inStream, outStream);
        }
    }

    public delegate void ConnectionMessageEventHandler<TRead, TWrite>(ObjectStreamConnection<TRead, TWrite> connection, TRead message)
        where TRead : class
        where TWrite : class;

    public delegate void ConnectionExceptionEventHandler<TRead, TWrite>(ObjectStreamConnection<TRead, TWrite> connection, Exception exception)
        where TRead : class
        where TWrite : class;
}
