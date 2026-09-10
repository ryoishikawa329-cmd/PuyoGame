using System;
using System.Collections.Generic;

namespace PuyoGame
{
    /// <summary>消去された1マスの情報。演出の表示位置と色に使う。</summary>
    public readonly struct ClearedCell
    {
        public readonly int X;
        public readonly int Y;
        public readonly PuyoColor Color;

        public ClearedCell(int x, int y, PuyoColor color)
        {
            X = x;
            Y = y;
            Color = color;
        }
    }

    /// <summary>
    /// 盤面に対する「浮きブロックの落下」「同色マッチの消去」「連鎖」を行う純粋なロジック。
    /// MonoBehaviour ではないので表示には依存しない。
    /// </summary>
    public static class BoardResolver
    {
        /// <summary>消えるのに必要な、一直線に連続する数。</summary>
        public const int DefaultMinLineLength = 3;

        /// <summary>
        /// 浮いているブロックを1マスだけ下に落とす。1つでも動いたら true。
        /// 後でアニメーションさせたい場合は、これを1フレームずつ呼べばよい。
        /// </summary>
        public static bool StepGravity(BoardData board)
        {
            bool moved = false;
            for (int x = 0; x < board.Width; x++)
            {
                // 下の段から見ていくので、落としたブロックを同じパスで二重に動かすことはない
                for (int y = 1; y < board.Height; y++)
                {
                    var color = board.Get(x, y);
                    if (color == PuyoColor.None) continue;
                    if (!board.IsEmpty(x, y - 1)) continue;

                    board.Set(x, y - 1, color);
                    board.Set(x, y, PuyoColor.None);
                    moved = true;
                }
            }
            return moved;
        }

        /// <summary>浮いているブロックがなくなるまで落とし切る。1つでも動いたら true。</summary>
        public static bool ApplyGravity(BoardData board)
        {
            bool any = false;
            while (StepGravity(board)) any = true;
            return any;
        }

        /// <summary>
        /// 同色が横一列または縦一列に minLineLength 個以上「連続」しているところを消す。
        /// L字や2x2のような一直線でない形は、つながっていても消えない。
        /// 縦横の両方が成立する十字などは、両方まとめて消える。消したマス数を返す。
        /// </summary>
        /// <param name="cleared">渡すと、消したマスの座標と色が積まれる（演出用）。</param>
        public static int ClearMatches(BoardData board, int minLineLength = DefaultMinLineLength,
                                       List<ClearedCell> cleared = null)
        {
            cleared?.Clear();

            int w = board.Width, h = board.Height;
            var marked = new bool[w, h];

            // 横方向：同じ色が続く区間を数え、規定数以上なら印を付ける
            for (int y = 0; y < h; y++)
            {
                int x = 0;
                while (x < w)
                {
                    var color = board.Get(x, y);
                    if (color == PuyoColor.None) { x++; continue; }

                    int end = x + 1;
                    while (end < w && board.Get(end, y) == color) end++;

                    if (end - x >= minLineLength)
                        for (int i = x; i < end; i++) marked[i, y] = true;

                    x = end;
                }
            }

            // 縦方向も同様
            for (int x = 0; x < w; x++)
            {
                int y = 0;
                while (y < h)
                {
                    var color = board.Get(x, y);
                    if (color == PuyoColor.None) { y++; continue; }

                    int end = y + 1;
                    while (end < h && board.Get(x, end) == color) end++;

                    if (end - y >= minLineLength)
                        for (int i = y; i < end; i++) marked[x, i] = true;

                    y = end;
                }
            }

            // 判定が終わってからまとめて消す（途中で消すと他の列の判定に影響するため）
            int count = 0;
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (!marked[x, y]) continue;
                    cleared?.Add(new ClearedCell(x, y, board.Get(x, y)));
                    board.Set(x, y, PuyoColor.None);
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 「落下 → 消去」を消えなくなるまで繰り返す。成立した連鎖数を返す（消えなければ0）。
        /// clearedPerChain を渡すと、各連鎖ステップで消えたマス数が順に積まれる（スコア計算用）。
        /// onStepCleared を渡すと、段ごとに (連鎖数, 消えたマス) が通知される（演出用）。
        /// 通知するリストは段ごとに使い回すため、受け取り側はその場で使い切ること。
        /// </summary>
        public static int Resolve(BoardData board, int minLineLength = DefaultMinLineLength,
                                  List<int> clearedPerChain = null,
                                  Action<int, IReadOnlyList<ClearedCell>> onStepCleared = null)
        {
            clearedPerChain?.Clear();

            var cells = onStepCleared != null ? new List<ClearedCell>() : null;

            ApplyGravity(board);          // 着地直後の浮きを先に解消する
            int chain = 0;
            while (true)
            {
                int cleared = ClearMatches(board, minLineLength, cells);
                if (cleared == 0) break;

                chain++;
                clearedPerChain?.Add(cleared);
                onStepCleared?.Invoke(chain, cells);
                ApplyGravity(board);
            }
            return chain;
        }
    }
}
