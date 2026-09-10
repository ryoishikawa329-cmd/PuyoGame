#!/bin/bash
# Build/WebGL の中身を gh-pages ブランチに載せ替えて push する。
#
# ビルド成果物は毎回まるごと変わるので、main の履歴には入れず、
# gh-pages ブランチに「最新だけ」を置く（履歴は毎回作り直す）。
#
#   使い方: Tools/deploy_gh_pages.sh
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$(pwd)"
BUILD="$ROOT/Build/WebGL"
BRANCH="gh-pages"

if [ ! -f "$BUILD/index.html" ]; then
  echo "ビルドが見つかりません: $BUILD/index.html" >&2
  echo "Unity で Tools > PuyoGame > WebGLビルドを作る を実行してください。" >&2
  exit 1
fi

if [ -z "$(git remote 2>/dev/null)" ]; then
  echo "リモートが設定されていません。先に GitHub のリポジトリを登録してください。" >&2
  exit 1
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

cp -R "$BUILD"/. "$WORK"/
# これが無いと、GitHub Pages が Jekyll として処理して一部ファイルを配信しない
touch "$WORK/.nojekyll"

cd "$WORK"
git init -q
git checkout -q -b "$BRANCH"
git add -A
git -c user.name="$(git -C "$ROOT" config user.name || echo 'PuyoGame')" \
    -c user.email="$(git -C "$ROOT" config user.email || echo 'noreply@example.com')" \
    commit -q -m "WebGLビルドを公開 ($(date '+%Y-%m-%d %H:%M'))"

REMOTE="$(git -C "$ROOT" remote get-url origin)"
git push -q --force "$REMOTE" "$BRANCH"

echo "gh-pages を更新しました: $REMOTE"
