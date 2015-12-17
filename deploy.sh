#!/bin/bash

ORG_NAME=${TRAVIS_REPO_SLUG%/*}
REPO_NAME=${TRAVIS_REPO_SLUG#*/}

function die_with { echo "$*" >&2; exit 1; }

echo "Are you Travis?"
if [ ! $TRAVIS ]; then die_with "You are not Travis!"; fi

echo "Checking if commit is a pull request"
if [ $TRAVIS_PULL_REQUEST == true ]; then die_with "Skipping deployment for pull request!"; fi

echo "Configuring git credentials"
git config --global user.email "travis@travis-ci.org" || die_with "Failed to configure git user email!"
git config --global user.name "Travis" || die_with "Failed to configure git user name!"

echo "Cloning Oxide repo using token"
GIT_REPO="https://$GITHUB_TOKEN@github.com/$ORG_NAME/Oxide.git"
git clone --depth 1 $GIT_REPO $HOME/Oxide >/dev/null || die_with "Failed to clone Oxide repository!"

cd $HOME/Oxide || die_with "Failed to change to Oxide directory!"

ATTEMPT=0
until [ $ATTEMPT -ge 5 ]; do
    echo "Fetching any changes from Oxide repository"
    git fetch $GIT_REPO || die_with "Failed to fetch changes to Oxide!"
    git reset --hard origin/master

    echo "Copying file(s) to Oxide directory"
    cp -vf $TRAVIS_BUILD_DIR/{CSharpCompiler,libmono-2.0.so.1} Extensions/Oxide.Ext.CSharp/Dependencies/Linux || die_with "Failed to copy changes to Oxide!"

    echo "Adding and committing changes"
    git add . || die_with "Failed to add files for commit!"
    COMMIT_MESSAGE="$REPO_NAME Linux build $TRAVIS_BUILD_NUMBER from https://github.com/$TRAVIS_REPO_SLUG/commit/${TRAVIS_COMMIT:0:7}"
    git commit -m "$COMMIT_MESSAGE" || die_with "Failed to commit files!"

    git config http.postBuffer 52428800
    git config pack.windowMemory "32m"
    git repack --max-pack-size=100M -a -d

    echo "Pushing build to GitHub"
    git push -q origin master >/dev/null && break || die_with "Failed to push to GitHub!"
    ATTEMPT=$[$ATTEMPT+1]
    sleep 15
done || die_with "Failed to deploy build!"

echo "Deployment cycle completed. Happy developing!"
