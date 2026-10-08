# サイトビジュアルレビュー（Claude Design 依頼前の現状把握）

対象: `docs/`（GitHub Pages）の `index.html` / `faq.html` / `compatibility.html` /
`privacy.html` と `styles.css` / `motion.js` / `preview.js`。

目的: 現在のビジュアル（ダーク基調・青アクセント・Discord カードのライブ
プレビュー）を維持したまま磨き上げる。全面リニューアルではない。

確認方法: Chromium で 1440×900（デスクトップ）と 390×844（モバイル）の全ページを
フルページ撮影し、CSS/JS を読んで原因箇所を特定した。両幅とも横スクロールは
発生していない。

## 優先度 A — 明確な不具合（直せば即座に見た目が良くなる）

1. **サブページのリード文が右にずれる**
   `.lead` の `margin: 1.15rem auto 0`（`styles.css:219-225`）がトップ用の中央寄せ。
   `.prose > .lead`（`styles.css:724`）は `margin-bottom` しか上書きしないため、
   780px のプロース幅の中で 550px のブロックが中央に寄り、左揃えの見出しと
   軸がずれる。Compatibility / FAQ / Data flow のデスクトップ表示で顕著。

2. **サブページのアイブロウ（小見出し）だけ灰色になる**
   `.prose p`（`styles.css:719`）の詳細度が `.eyebrow`（`styles.css:181`）に勝ち、
   トップでは青い eyebrow がサブページでは `--text-secondary` の灰色になる。
   さらにサブページは `FACTS AND LIMITS` のような全大文字、トップは
   `Why I made it` の文頭大文字で、表記も揃っていない。

3. **動作要件の区切り「·」が見えない／折り返すと字下げされる**
   `.install-requirements span + span::before`（`styles.css:698-702`）の色が
   `--border-subtle`（白 5%）なので、区切りが実質見えず、間隔がばらついて
   見える。モバイルで折り返すと、行頭に来た「·」と `margin-right` のせいで
   `Discord Desktop` が字下げされる。

4. **モバイルで「Download for Windows」ボタンが 2 行になる**
   `.actions` の幅が `min(100%, 12.5rem)`（`styles.css:1169`）に固定されているため、
   主要 CTA が 2 行の大きな楕円になり、下の `View source` と高さが揃わない。

5. **アニメーションを減らす設定（prefers-reduced-motion）に対応していない**
   `styles.css` / `motion.js` / `preview.js` のどこにも `prefers-reduced-motion` がない。
   常時動いているもの: 背景の漂い（`body::before`、28 秒）、カードの浮遊（15 秒）、
   ステージの発光（17 秒）、ブランドマークの脈動（7.2 秒）、スクロール時の表示
   アニメーション、カルーセルの自動送り。WCAG 2.3.3 / 2.2.2 の観点で、
   「設定時は止める／即時表示」にすべき。

## 優先度 B — 磨き込み（ビジュアル品質）

6. **ヒーロー見出しの改行が不格好**
   1440px では「Show the work behind the / status.」と最後の 1 語だけが 2 行目に
   落ちる。`h1`（`styles.css:197`）に `text-wrap: balance` を指定するか、
   幅を調整して 2 行のバランスを取る。

7. **モバイルの Discord カードが小さすぎて読めない**
   モバイルでは `--rpc-preview-active-scale: 0.7`（`styles.css:1034`）のため、13px の
   本文が実質約 9px になる。サイトの見どころなので、モバイルではカードを
   等倍に近づけ、左右のチラ見せを減らす方向で再検討する。

8. **ページごとにヘッダーとフッターが違う**
   - ブランドの青いドット（`.brand-mark`）はトップにしかない。
   - ナビ項目がページごとに入れ替わる（トップには Home がなく、サブページには
     ある）。現在のページを示す表示（`aria-current`）もない。
   - フッターは、トップが `nav.footer-links`（下線なし）なのに、サブページは
     単独の `<a>` で、既定の下線付きリンクになっている。
   共通のヘッダー・フッターに統一し、現在ページを強調表示する。

9. **縦の並び方の軸が揃っていない**
   ヒーローは中央揃え、Origin は左に eyebrow 列がある 2 カラム、Install は
   3 カラムと、セクションごとに並びの軸が変わる。区切り線も Origin の下に
   1 本あるだけ。セクション間の余白とグリッドを揃えると、全体がまとまる。
   モバイルの Origin では、eyebrow と見出しの間が 2.2rem（`styles.css:990`）も
   空き、eyebrow が浮いて見える。

10. **トップのボリュームが薄い**
    ヒーロー → 制作理由 → ダウンロードの 3 ブロックだけで、「どんな表示に
    なるのか」（思考要約、`MCP chrome-devtools`、`Editing README.md`、
    パーティー人数、`gpt 5.6 luna xhigh 1.5x`）の具体例が、カルーセルにしか
    ない。既存の表示ルールを短いフィーチャー帯やギャラリーとして見せる
    余地がある（※コンテンツ追加になるので任意）。

11. **FAQ が読みにくい**
    質問がプロースの `h2`（最大 2.2rem）で、回答に対して見出しが重すぎる。
    質問と回答のまとまりが弱く、拾い読みしづらい。小さめの見出しと区切り、
    または `<details>` のアコーディオンを検討する。
    リード文「Answers are intentionally specific so users and search assistants
    can distinguish…」は SEO 向けのメモのような文章で、利用者に向けた言葉に
    なっていない。

12. **SNS 共有時のカード画像がない**
    `og:image` がアプリアイコン（`rpc_codex.png`）の流用で、1200×630 の
    共有画像がない。サブページには OG タグ自体がない。favicon も
    `raw.githubusercontent.com` から外部参照している。

13. **細部**
    - `discord-presence-for-codex.exe` の `code` チップが、モバイルで語の途中で
      折り返される。
    - サブページ下部の余白が大きい（`.section` の padding と `.site-footer` の
      margin-top が重なる）。

## 優先度 C — デザイントークンの整理（Claude Design での編集性）

Claude Design で一貫して編集できるよう、ハードコードされた値をトークン化する。

- 色: `h2 { color: #ffffff }`（`styles.css:211`）、`a:hover { color: #bfdbfe }`（`:83`）、
  カードの `#18181a` / `#b5bac1` / `#f2f3f5` / `#7ec191`、ナビやモバイル用の
  `rgba(...)` 多数。
- 余白・角丸・影: 段階（スケール）が定義されておらず、`0.62rem`、`1.45rem`、
  `11px`、`14px` などの値が散在している。
- 文字サイズ: `clamp()` が要素ごとに個別に書かれている。見出しと本文の段階を
  変数にまとめる。

## コントラスト（WCAG AA、背景 `#08090d`）

| 用途 | 色 | 比 | 判定 |
| --- | --- | --- | --- |
| 本文（secondary） | `#9aa2b4` | 7.77 | OK |
| ナビ・フッター（muted） | `#747d91` | 4.82 | 合格だが余裕は小さい |
| eyebrow | `#60a5fa` | 7.83 | OK |
| リンク | `#64b5f6` | 8.99 | OK |
| 主要ボタン（白／`#0071e3`） | — | 4.70 | 合格だが余裕は小さい |
| カード本文（`#b5bac1`／`#18181a`） | — | 9.08 | OK |

フォーカスリングは、リンク・ボタン・カルーセル操作の全てにある。カルーセルは
ホバーとフォーカスで自動送りが止まる。

## 変えないもの（Claude Design への制約）

- ダーク基調・青アクセントの方向性と、`:root` の既存トークン名。
- Discord カードの 360×148 の比率、100×100 の大画像、32×32 の円形小画像、
  経過時間の表示、GIF アニメーション（`docs/assets/*.gif` は GIF のまま）。
- `preview.js` / `motion.js` が参照する属性とクラス:
  `data-rpc-*`、`data-motion`、`.rpc-preview-*`、`.site-header.is-scrolled`、
  `html.motion-enhanced`。
- 各ページの `<title>`、`meta description`、canonical、JSON-LD、`sitemap.xml`、
  `robots.txt`、`llms.txt`、ファイル名と URL。
- 非公式プロジェクトであることの明記と、`K's Codex RPC` の名称。

## Claude Design への依頼文（貼り付け用）

```
Codex Discord Rich Presence の紹介サイト（4ページ、静的 HTML/CSS）を、
現在のビジュアルを保ったまま磨いてください。全面リニューアルではありません。

維持: ダーク背景 #08090d、青アクセント #2997ff、システムフォント、
Discord アクティビティカード（360×148、GIF アートワーク、経過時間）を中心に
据えたヒーロー、既存の文言と情報構成。

改善してほしい点:
1. ヘッダー／フッターを全ページで共通化（ブランドドット、固定ナビ順、現在ページ表示）
2. サブページ（FAQ / Compatibility / Data flow）の見出し・リード・eyebrow を左揃えで統一し、
   eyebrow はトップと同じ青・文頭大文字に
3. ヒーロー見出しの改行バランス、モバイルで CTA が 1 行に収まるボタン幅
4. モバイルでも Discord カードが読める大きさ（本文 12px 以上）
5. 動作要件（Windows x64 / .NET 9 Desktop Runtime / Discord Desktop）を
   見える区切りのチップ表示に
6. セクション間の余白とグリッド軸の統一
7. FAQ を拾い読みしやすい Q&A レイアウトに
8. 1200×630 の OG 画像

アートボード: デスクトップ 1440 幅とモバイル 390 幅で、トップと FAQ を各 1 枚ずつ。
色・余白・角丸・文字サイズはトークンとして定義してください。
```
