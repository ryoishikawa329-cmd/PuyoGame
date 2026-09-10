using System;

namespace PuyoGame
{
    /// <summary>
    /// 盤面をデータとして保持するクラス。MonoBehaviour ではないので描画には依存しない。
    /// 座標系は左下が (0, 0)、x が右方向、y が上方向。
    /// </summary>
    public class BoardData
    {
        public const int DefaultWidth = 4;
        public const int DefaultHeight = 10;

        readonly PuyoColor[,] cells;

        public int Width { get; }
        public int Height { get; }

        public BoardData(int width = DefaultWidth, int height = DefaultHeight)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            cells = new PuyoColor[width, height];
        }

        /// <summary>指定座標が盤面の内側かどうか。</summary>
        public bool IsInside(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        /// <summary>指定座標の色を取得する。盤外は None を返す。</summary>
        public PuyoColor Get(int x, int y)
        {
            return IsInside(x, y) ? cells[x, y] : PuyoColor.None;
        }

        /// <summary>指定座標に色を書き込む。盤外なら何もせず false を返す。</summary>
        public bool Set(int x, int y, PuyoColor color)
        {
            if (!IsInside(x, y)) return false;
            cells[x, y] = color;
            return true;
        }

        /// <summary>指定座標が空マスかどうか。盤外は false（置けない）扱い。</summary>
        public bool IsEmpty(int x, int y)
        {
            return IsInside(x, y) && cells[x, y] == PuyoColor.None;
        }

        /// <summary>盤面を全て空にする。</summary>
        public void Clear()
        {
            Array.Clear(cells, 0, cells.Length);
        }

        /// <summary>デバッグ用。上の行から順に文字で並べた文字列を返す。</summary>
        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            for (int y = Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < Width; x++)
                {
                    sb.Append(ToChar(cells[x, y]));
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static char ToChar(PuyoColor color)
        {
            switch (color)
            {
                case PuyoColor.Photo1: return '1';
                case PuyoColor.Photo2: return '2';
                case PuyoColor.Photo3: return '3';
                default: return '.';
            }
        }
    }
}
