using UnityEngine;

namespace PuyoGame
{
    /// <summary>
    /// 盤面が画面いっぱいに収まるよう、実行中の画面比率に合わせてカメラの画角を決める。
    /// 盤面の大きさが先で、カメラが後から合わせる。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class BoardCameraFitter : MonoBehaviour
    {
        [SerializeField] BoardView board;

        [Tooltip("盤面の横幅が画面幅に占める割合の目安")]
        [Range(0.5f, 1f)]
        [SerializeField] float widthFill = 0.85f;
        [Tooltip("盤面の高さが画面高さに占める割合の上限")]
        [Range(0.5f, 1f)]
        [SerializeField] float heightFill = 0.88f;
        [Tooltip("画面下部に操作ボタン用として空けておく割合。盤面はその分だけ上に寄る")]
        [Range(0f, 0.35f)]
        [SerializeField] float bottomReserve = 0.15f;

        Camera cam;
        float lastAspect;
        int lastWidth, lastHeight;
        float lastBoardWidth, lastBoardHeight;

        void OnEnable()
        {
            Apply();
        }

        void LateUpdate()
        {
            if (board == null) return;

            float bw = board.Width * board.CellSize;
            float bh = board.Height * board.CellSize;

            // 画面か盤面が変わったときだけ計算し直す
            if (Screen.width == lastWidth && Screen.height == lastHeight
                && Mathf.Approximately(Aspect, lastAspect)
                && Mathf.Approximately(bw, lastBoardWidth)
                && Mathf.Approximately(bh, lastBoardHeight)) return;

            Apply();
        }

        float Aspect
        {
            get
            {
                if (cam == null) cam = GetComponent<Camera>();
                return cam != null && cam.aspect > 0.01f ? cam.aspect : 9f / 19.5f;
            }
        }

        /// <summary>盤面が収まる画角を計算して反映する。</summary>
        public void Apply()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null || board == null || !cam.orthographic) return;

            cam.orthographicSize = ComputeSize(
                board.Width * board.CellSize, board.Height * board.CellSize, Aspect);

            // 下に空けた分だけカメラを下げると、盤面が画面の上寄りに映る
            float shift = cam.orthographicSize * Mathf.Clamp01(bottomReserve);
            cam.transform.position = new Vector3(
                board.transform.position.x,
                board.transform.position.y - shift,
                cam.transform.position.z != 0f ? cam.transform.position.z : -10f);

            lastWidth = Screen.width;
            lastHeight = Screen.height;
            lastAspect = Aspect;
            lastBoardWidth = board.Width * board.CellSize;
            lastBoardHeight = board.Height * board.CellSize;
        }

        /// <summary>
        /// 横幅を埋めるのに必要な大きさと、縦に収めるのに必要な大きさの、厳しい方を採る。
        /// 縦長画面では横幅が、横長画面では高さが効く。
        /// 高さは、操作ボタン用に空ける分を除いた範囲で数える。
        /// </summary>
        public float ComputeSize(float boardWidth, float boardHeight, float aspect)
        {
            float usable = Mathf.Max(1f - Mathf.Clamp01(bottomReserve), 0.1f);
            float fromWidth = (boardWidth / Mathf.Max(widthFill, 0.01f)) / (2f * Mathf.Max(aspect, 0.01f));
            float fromHeight = (boardHeight / Mathf.Max(heightFill, 0.01f)) / (2f * usable);
            return Mathf.Max(fromWidth, fromHeight);
        }

        /// <summary>画面下部に空けている割合（動作確認用）。</summary>
        public float BottomReserve => bottomReserve;
    }
}
