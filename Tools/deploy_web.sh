#!/bin/bash
# Unity の WebGL ビルド結果を docs/ に反映して push する。
#
# docs/ は main ブランチに置く。Netlify も GitHub Pages も
# 「main ブランチの docs フォルダ」1か所だけを見ればよくなる。
#
#   使い方: Tools/deploy_web.sh
set -euo pipefail

cd "$(dirname "$0")/.."
BUILD="Build/WebGL"
DOCS="docs"

if [ ! -f "$BUILD/index.html" ]; then
  echo "ビルドが見つかりません: $BUILD/index.html" >&2
  echo "Unity で Tools > PuyoGame > WebGLビルドを作る を実行してください。" >&2
  exit 1
fi

# 古いビルドの残骸が混ざらないよう、いったん空にしてから入れ替える
rm -rf "$DOCS"
mkdir -p "$DOCS"
cp -R "$BUILD"/. "$DOCS"/
# これが無いと、GitHub Pages が Jekyll として処理して一部ファイルを配信しない
touch "$DOCS/.nojekyll"

# ソース側の変更が置き去りにならないよう知らせる（docs だけをコミットするため）
if [ -n "$(git status --porcelain -- . ":(exclude)$DOCS")" ]; then
  echo "注意: docs 以外に未コミットの変更があります。別途コミットしてください:" >&2
  git status --short -- . ":(exclude)$DOCS" >&2
fi

git add -A "$DOCS"
if git diff --cached --quiet; then
  echo "ビルド結果に変化がないので、コミットしませんでした。"
  exit 0
fi

git commit -q -m "WebGLビルドを公開 ($(date '+%Y-%m-%d %H:%M'))"
git push -q origin HEAD
echo "docs/ を更新して push しました。"
