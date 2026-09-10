using System.Collections.Generic;
using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// 盤面のまわりに影と枠を描いて、背景から浮き上がって見えるようにする。
    /// 盤面の形状は BoardView から取得し、装飾用のスプライトはその場で生成する。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoardView))]
    public class BoardFrameView : MonoBehaviour
    {
        [Header("枠")]
        [Tooltip("盤面の外側に出す枠の太さ（マス単位）")]
        [Min(0f)]
        [SerializeField] float frameThickness = 0.32f;
        [SerializeField] Color frameTop = new Color(0.42f, 0.45f, 0.62f);
        [SerializeField] Color frameBottom = new Color(0.20f, 0.21f, 0.32f);
        [Tooltip("枠の内側、盤面の下に敷く面の色")]
        [SerializeField] Color innerPanel = new Color(0.13f, 0.13f, 0.19f, 0.96f);

        [Header("影")]
        [Tooltip("影の広がり（マス単位）")]
        [Min(0f)]
        [SerializeField] float shadowSpread = 0.55f;
        [SerializeField] Vector2 shadowOffset = new Vector2(0.10f, -0.16f);
        [SerializeField] Color shadowColor = new Color(0f, 0f, 0f, 0.38f);

        [Header("光沢")]
        [Tooltip("枠の上辺に入れるハイライトの強さ")]
        [Range(0f, 1f)]
        [SerializeField] float glossStrength = 0.35f;

        [Header("描画順")]
        [SerializeField] int shadowOrder = -30;
        [SerializeField] int frameOrder = -20;
        [SerializeField] int panelOrder = -15;
        [SerializeField] int glossOrder = -12;

        const float DecorPixelsPerUnit = 24f;

        BoardView board;
        Transform root;
        readonly List<Sprite> generated = new List<Sprite>();
        readonly List<Material> materials = new List<Material>();

        void OnEnable()
        {
            Rebuild();
        }

        void OnDisable()
        {
            Teardown();
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
            Rebuild();
        }
#endif

        /// <summary>枠と影を作り直す。</summary>
        public void Rebuild()
        {
            Teardown();
            if (board == null) board = GetComponent<BoardView>();
            if (board == null) return;

            var rootObject = new GameObject("BoardFrame (generated)");
            rootObject.hideFlags = HideFlags.DontSave;
            root = rootObject.transform;
            root.SetParent(transform, false);

            float cell = board.CellSize;
            Vector2 boardSize = new Vector2(board.Width * cell, board.Height * cell);
            Vector2 frameSize = boardSize + Vector2.one * (frameThickness * cell * 2f);
            Vector2 shadowSize = frameSize + Vector2.one * (shadowSpread * cell * 2f);
            Vector3 center = transform.position;

            // 影：枠より一回り大きい、縁がぼけた矩形を少しずらして敷く
            var shadow = ProceduralSprites.SoftRect(
                Px(shadowSize.x), Px(shadowSize.y),
                cornerRadius: Px(0.55f * cell), softness: Px(shadowSpread * cell * 1.6f), name: "Shadow");
            Place("Shadow", shadow, shadowSize,
                  center + new Vector3(shadowOffset.x * cell, shadowOffset.y * cell, 0f),
                  shadowColor, shadowOrder);

            // 枠：上から下へのグラデーションで、金属や樹脂のような質感にする
            var frame = ProceduralSprites.VerticalGradient(frameTop, frameBottom, 64, "Frame");
            Place("Frame", frame, frameSize, center, Color.white, frameOrder);

            // 枠の内側の面。マス目より暗くして盤面を締める
            var panel = ProceduralSprites.SoftRect(
                Px(boardSize.x), Px(boardSize.y),
                cornerRadius: Px(0.12f * cell), softness: 2f, name: "Panel");
            Place("Panel", panel, boardSize, center, innerPanel, panelOrder);

            // 上辺の細いハイライトで光沢を出す
            if (glossStrength > 0f)
            {
                var gloss = ProceduralSprites.VerticalGradient(
                    new Color(1f, 1f, 1f, glossStrength), new Color(1f, 1f, 1f, 0f), 32, "Gloss");
                float h = frameThickness * cell * 0.75f;
                Place("Gloss", gloss, new Vector2(frameSize.x * 0.985f, h),
                      center + new Vector3(0f, frameSize.y * 0.5f - h * 0.5f, 0f),
                      Color.white, glossOrder);
            }
        }

        static int Px(float worldSize) => Mathf.Max(4, Mathf.RoundToInt(worldSize * DecorPixelsPerUnit));

        void Place(string name, Sprite sprite, Vector2 worldSize, Vector3 position, Color color, int order)
        {
            generated.Add(sprite);

            var go = new GameObject(name);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(root, false);
            go.transform.position = position;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;

            // スプライトごとにマテリアルを分けないと、どれも同じテクスチャで描かれてしまう
            if (sr.sharedMaterial != null)
            {
                var mat = new Material(sr.sharedMaterial)
                {
                    name = $"PuyoGame_Frame_{name}",
                    hideFlags = HideFlags.DontSave,
                };
                sr.sharedMaterial = mat;
                materials.Add(mat);
            }

            var size = sprite.bounds.size;
            go.transform.localScale = new Vector3(
                size.x > 0f ? worldSize.x / size.x : 1f,
                size.y > 0f ? worldSize.y / size.y : 1f,
                1f);
        }

        void Teardown()
        {
            if (root != null)
            {
                DestroySafely(root.gameObject);
                root = null;
            }
            else
            {
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    var child = transform.GetChild(i);
                    if (child.name == "BoardFrame (generated)") DestroySafely(child.gameObject);
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
    }
}
