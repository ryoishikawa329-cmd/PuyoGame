using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// 装飾用のスプライトをその場で作るユーティリティ。
    /// 画像アセットを増やさずに、影・グラデーション・光の玉を用意する。
    /// </summary>
    public static class ProceduralSprites
    {
        /// <summary>角丸で縁がぼけた矩形。影やパネルに使う。</summary>
        public static Sprite SoftRect(int width, int height, float cornerRadius, float softness,
                                      string name = "SoftRect")
        {
            width = Mathf.Max(4, width);
            height = Mathf.Max(4, height);
            var tex = NewTexture(width, height, name);
            var px = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // 角丸矩形からの符号付き距離を求め、外に向かって滑らかに減衰させる
                    float dx = Mathf.Abs(x + 0.5f - width * 0.5f) - (width * 0.5f - cornerRadius);
                    float dy = Mathf.Abs(y + 0.5f - height * 0.5f) - (height * 0.5f - cornerRadius);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f)
                                             + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float dist = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - cornerRadius;

                    float a = Mathf.Clamp01(0.5f - dist / Mathf.Max(softness, 0.001f));
                    a = a * a * (3f - 2f * a);          // なめらかに
                    px[y * width + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return Finish(tex, px, name);
        }

        /// <summary>上から下へのグラデーション。枠や背景の陰影に使う。</summary>
        public static Sprite VerticalGradient(Color top, Color bottom, int height = 64,
                                              string name = "VGradient")
        {
            const int width = 4;
            var tex = NewTexture(width, height, name);
            var px = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                var c = Color.Lerp(bottom, top, y / (float)(height - 1));
                for (int x = 0; x < width; x++) px[y * width + x] = c;
            }
            return Finish(tex, px, name);
        }

        /// <summary>中心が明るく外へ向かって消える円。ぼんやりした光に使う。</summary>
        public static Sprite RadialGlow(int size = 128, float falloff = 2.2f, string name = "Glow")
        {
            size = Mathf.Max(8, size);
            var tex = NewTexture(size, size, name);
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                    float a = Mathf.Clamp01(1f - d);
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Pow(a, falloff));
                }
            }
            return Finish(tex, px, name);
        }

        static Texture2D NewTexture(int w, int h, string name)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = $"PuyoGame_{name}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
        }

        static Sprite Finish(Texture2D tex, Color[] px, string name)
        {
            tex.SetPixels(px);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                       new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = $"PuyoGame_{name}";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        /// <summary>生成したスプライトとテクスチャをまとめて破棄する。</summary>
        public static void Destroy(Sprite sprite)
        {
            if (sprite == null) return;
            var tex = sprite.texture;
            if (Application.isPlaying)
            {
                Object.Destroy(sprite);
                if (tex != null) Object.Destroy(tex);
            }
            else
            {
                Object.DestroyImmediate(sprite);
                if (tex != null) Object.DestroyImmediate(tex);
            }
        }
    }
}
