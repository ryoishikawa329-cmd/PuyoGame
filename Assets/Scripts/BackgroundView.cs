using System.Collections.Generic;
using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// 背景スプライトをカメラの表示範囲に合わせて敷き、
    /// その上に緩いグラデーションとぼんやりした光を重ねて奥行きを出す。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public class BackgroundView : MonoBehaviour
    {
        [Tooltip("未指定なら Camera.main を使う")]
        [SerializeField] Camera targetCamera;
        [Tooltip("盤面（0以上）より奥に描くため負の値にする")]
        [SerializeField] int sortingOrder = -100;

        [Header("グラデーション")]
        [Tooltip("上から下へ暗くして、盤面まわりを落ち着かせる")]
        [SerializeField] bool useGradient = true;
        [SerializeField] Color gradientTop = new Color(0.35f, 0.42f, 0.70f, 0.10f);
        [SerializeField] Color gradientBottom = new Color(0.04f, 0.03f, 0.10f, 0.45f);

        [Header("光の演出")]
        [Tooltip("四隅にぼんやりした光を置いて単調さを消す")]
        [SerializeField] bool useGlows = true;
        [Tooltip("未指定なら円形のグラデーションを生成する")]
        [SerializeField] Sprite glowSprite;
        [SerializeField] Color glowWarm = new Color(1f, 0.92f, 0.70f, 0.28f);
        [SerializeField] Color glowCool = new Color(0.68f, 0.84f, 1f, 0.24f);

        // 画面サイズに対する配置。x,y は中心からの割合、z は大きさの割合。
        static readonly Vector3[] GlowLayout =
        {
            new Vector3(-0.40f,  0.38f, 0.85f),
            new Vector3( 0.42f,  0.26f, 0.60f),
            new Vector3(-0.36f, -0.34f, 0.70f),
            new Vector3( 0.38f, -0.42f, 0.95f),
        };

        SpriteRenderer spriteRenderer;
        Transform decorRoot;
        SpriteRenderer gradientRenderer;
        readonly List<SpriteRenderer> glowRenderers = new List<SpriteRenderer>();
        readonly List<Sprite> generated = new List<Sprite>();
        readonly List<Material> materials = new List<Material>();

        int lastWidth, lastHeight;
        float lastOrthoSize, lastAspect;

        void OnEnable()
        {
            BuildDecor();
            Apply();
        }

        void OnDisable()
        {
            TeardownDecor();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += RebuildFromInspector;
        }

        void RebuildFromInspector()
        {
            UnityEditor.EditorApplication.delayCall -= RebuildFromInspector;
            if (this == null || !isActiveAndEnabled) return;
            BuildDecor();
            Apply();
        }
#endif

        void LateUpdate()
        {
            var cam = ResolveCamera();
            if (cam == null) return;

            if (Screen.width == lastWidth && Screen.height == lastHeight
                && Mathf.Approximately(cam.orthographicSize, lastOrthoSize)
                && Mathf.Approximately(cam.aspect, lastAspect)) return;

            Apply();
        }

        /// <summary>カメラの表示範囲を覆うようにスケールと位置を合わせる。</summary>
        public void Apply()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            var cam = ResolveCamera();
            if (cam == null || !cam.orthographic) return;

            float viewHeight = cam.orthographicSize * 2f;
            float viewWidth = viewHeight * Mathf.Max(cam.aspect, 0.01f);
            Vector3 center = new Vector3(cam.transform.position.x, cam.transform.position.y,
                                         transform.position.z);

            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                spriteRenderer.sortingOrder = sortingOrder;
                var size = spriteRenderer.sprite.bounds.size;
                if (size.x > 0f && size.y > 0f)
                {
                    // 縦横どちらにも隙間ができないよう、大きい方の倍率に合わせる
                    float scale = Mathf.Max(viewWidth / size.x, viewHeight / size.y);
                    transform.localScale = new Vector3(scale, scale, 1f);
                    transform.position = center;
                }
            }

            if (gradientRenderer != null)
                Fit(gradientRenderer, center, new Vector2(viewWidth * 1.02f, viewHeight * 1.02f));

            for (int i = 0; i < glowRenderers.Count && i < GlowLayout.Length; i++)
            {
                var layout = GlowLayout[i];
                float size = viewHeight * layout.z;
                Fit(glowRenderers[i],
                    center + new Vector3(viewWidth * layout.x, viewHeight * layout.y, 0f),
                    new Vector2(size, size));
            }

            lastWidth = Screen.width;
            lastHeight = Screen.height;
            lastOrthoSize = cam.orthographicSize;
            lastAspect = cam.aspect;
        }

        void BuildDecor()
        {
            TeardownDecor();

            var rootObject = new GameObject("BackgroundDecor (generated)");
            rootObject.hideFlags = HideFlags.DontSave;
            decorRoot = rootObject.transform;
            decorRoot.SetParent(transform.parent, false);   // 背景のスケールに引きずられないよう外に置く

            if (useGradient)
            {
                var sprite = ProceduralSprites.VerticalGradient(gradientTop, gradientBottom, 96, "BgGradient");
                generated.Add(sprite);
                gradientRenderer = CreateRenderer("Gradient", sprite, Color.white, sortingOrder + 5);
            }

            if (useGlows)
            {
                var sprite = glowSprite;
                if (sprite == null)
                {
                    sprite = ProceduralSprites.RadialGlow(128, 2.4f, "BgGlow");
                    generated.Add(sprite);
                }
                for (int i = 0; i < GlowLayout.Length; i++)
                {
                    var color = (i % 2 == 0) ? glowWarm : glowCool;
                    glowRenderers.Add(CreateRenderer($"Glow_{i}", sprite, color, sortingOrder + 6));
                }
            }
        }

        SpriteRenderer CreateRenderer(string name, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(decorRoot, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;

            // スプライトごとにマテリアルを分けないと、同じテクスチャで描かれてしまう
            if (sr.sharedMaterial != null)
            {
                var mat = new Material(sr.sharedMaterial)
                {
                    name = $"PuyoGame_Bg_{name}",
                    hideFlags = HideFlags.DontSave,
                };
                sr.sharedMaterial = mat;
                materials.Add(mat);
            }
            return sr;
        }

        static void Fit(SpriteRenderer sr, Vector3 position, Vector2 worldSize)
        {
            if (sr == null || sr.sprite == null) return;
            var size = sr.sprite.bounds.size;
            sr.transform.position = position;
            sr.transform.localScale = new Vector3(
                size.x > 0f ? worldSize.x / size.x : 1f,
                size.y > 0f ? worldSize.y / size.y : 1f,
                1f);
        }

        void TeardownDecor()
        {
            gradientRenderer = null;
            glowRenderers.Clear();

            if (decorRoot != null)
            {
                DestroySafely(decorRoot.gameObject);
                decorRoot = null;
            }
            else
            {
                // ドメインリロードなどで参照が切れた場合の取り残しを掃除する
                var orphan = GameObject.Find("BackgroundDecor (generated)");
                while (orphan != null)
                {
                    DestroySafely(orphan);
                    orphan = GameObject.Find("BackgroundDecor (generated)");
                }
            }

            foreach (var m in materials) DestroySafely(m);
            materials.Clear();

            foreach (var s in generated) ProceduralSprites.Destroy(s);
            generated.Clear();
        }

        static void DestroySafely(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        Camera ResolveCamera() => targetCamera != null ? targetCamera : Camera.main;
    }
}
