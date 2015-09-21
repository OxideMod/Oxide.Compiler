#ifdef _WIN32
static char* toutf8(const wchar_t *s)
{
	int len_needed = WideCharToMultiByte(CP_UTF8, 0, s, -1, NULL, 0, NULL, NULL);
	char *news = (char*) malloc(len_needed);
	WideCharToMultiByte(CP_UTF8, 0, s, -1, news, len_needed, NULL, NULL);
	return news;
}
#endif

int main (int argc, char* argv[])
{
	char **newargs;
	int i, k = 0;
	SetEnvironmentVariable("MONO_EXTERNAL_ENCODINGS", "UTF-16");
	SetEnvironmentVariable("MONO_LOG_MASK", "");
	SetEnvironmentVariable("MONO_LOG_LEVEL", "");

#ifdef _WIN32
	/* CommandLineToArgvW() might return a different argc than the
	 * one passed to main(), so let it overwrite that, as we won't
	 * use argv[] on Windows anyway.
	 */
	wchar_t **wargv = CommandLineToArgvW (GetCommandLineW (), &argc);
#endif

	newargs = (char **) malloc (sizeof (char *) * (argc + 2) + count_mono_options_args ());

#ifdef _WIN32
	newargs [k++] = toutf8 (wargv [0]);
#else
	newargs [k++] = argv [0];
#endif

	if (mono_options != NULL) {
		i = 0;
		while (mono_options[i] != NULL)
			newargs[k++] = mono_options[i++];
	}

	newargs [k++] = image_name;

	for (i = 1; i < argc; i++) {
#ifdef _WIN32
		newargs [k++] = toutf8 (wargv [i]);
		size_t len = strlen(newargs [k-1]);
		char *str2 = malloc(len + 1 + 1 ); /* one for extra char, one for trailing zero */
		strcpy(str2, newargs [k-1]);
		str2[len] = ' ';
		str2[len + 1] = '\0';
		newargs [k-1] = str2;
#else
		newargs [k++] = argv [i];
#endif
	}
#ifdef _WIN32
	LocalFree (wargv);
#endif
	newargs [k] = NULL;

	if (config_dir != NULL && getenv ("MONO_CFG_DIR") == NULL)
		mono_set_dirs (getenv ("MONO_PATH"), config_dir);

	mono_mkbundle_init();

	return mono_main (k, newargs);
}