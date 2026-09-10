# 音声素材について（未取得）

BGM と効果音は **自動ダウンロードを行っていません**。各サイトが明示的に禁止しているためです。

## DOVA-SYNDROME（BGM）

サイト利用規約 https://dova-s.jp/_contents/agreement/ の「禁止事項」より:

> 3. bot等プログラムを使用して当サイトのコンテンツ（画像・音源など）を自動的に収集する行為

スクリプトによる取得はこれに該当するため実施していません。
**手動でのダウンロードは規約上まったく問題ありません**（商用利用可・著作権表記不要）。

- 明るくポップなループ向きBGMの検索: https://dova-s.jp/bgm/search.html
  （詳細検索でジャンル「ポップス」、イメージ「明るい」「楽しい」等を指定）
- 音源利用ライセンス: https://dova-s.jp/_contents/license/
- 注意: 楽曲ごとに作曲者が個別条件を設定している場合があるため、
  ダウンロード前に各楽曲ページのライセンス欄を必ず確認すること
- 2026-09-15 に「OpenTracks」へ名称変更予定

ダウンロードしたファイルは `Assets/Audio/BGM/` に配置してください。

## 効果音ラボ（SE）

robots.txt https://soundeffect-lab.info/robots.txt に以下の記述があります:

```
User-agent: ClaudeBot
Disallow: /*.mp3$
```

`GPTBot` `ChatGPT-User` `Google-Extended` `PerplexityBot` なども同様に mp3 取得を拒否。
AIエージェントによる音源取得を明確に拒否する意思表示のため、実施していません。

利用規約 https://soundeffect-lab.info/agreement/ より、**人が手動で取得して使う分には問題ありません**:
- 商用利用無料、クレジット表記・リンク不要
- アプリに操作音として組み込むことは「再配布に該当しない」と明記
- 禁止: 効果音ファイルそのものの再配布、AI学習データとしての利用、効果音を自由に鳴らせるアプリ

ダウンロードしたファイルは `Assets/Audio/SE/` に配置してください。

### 用途別の候補（各ページで試聴して選んでください）

| 用途 | 素材名 | ページ |
|---|---|---|
| 移動音（軽いクリック） | カーソル移動1「クセの少ない電子音」 | https://soundeffect-lab.info/sound/button/ |
| 移動音（軽いクリック） | 決定ボタンを押す22「ピコッ。可愛らしい」 | https://soundeffect-lab.info/sound/button/ |
| 移動音（軽いクリック） | 決定ボタンを押す31「カチッ」 | https://soundeffect-lab.info/sound/button/ |
| 着地音（ポトッ） | 決定ボタンを押す34「ポン。柔らかい音」 | https://soundeffect-lab.info/sound/button/ |
| 着地音（ポトッ） | 決定ボタンを押す52「ポン」 | https://soundeffect-lab.info/sound/button/ |
| 消える音（ポップ） | 決定ボタンを押す26「ポップなイメージ」 | https://soundeffect-lab.info/sound/button/ |
| 消える音（ポップ） | パッ「可愛くメッセージ表示」 | https://soundeffect-lab.info/sound/anime/ |
| 消える音（ポップ） | パパッ「柔らかい演出音」 | https://soundeffect-lab.info/sound/anime/ |
| 連鎖音（派手） | きらきら輝く1「美しさの演出に」 | https://soundeffect-lab.info/sound/anime/ |
| 連鎖音（派手） | 可愛く輝く1「魔法の音にも使える」 | https://soundeffect-lab.info/sound/anime/ |
| 連鎖音（派手） | レベルアップ「テッテレー」 | https://soundeffect-lab.info/sound/anime/ |
| ゲームオーバー | 呪いの旋律「デデデーン。失敗演出などに」 | https://soundeffect-lab.info/sound/anime/ |
| ゲームオーバー | クイズ不正解2「残念でした」 | https://soundeffect-lab.info/sound/anime/ |

連鎖音は段階別に「可愛く輝く1」→「きらきら輝く1」→「レベルアップ」と
派手さを上げていくと連鎖の高揚感が出せます。
