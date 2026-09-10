#!/usr/bin/env python3
"""顔写真を、ぷよぷよ風の丸いブロック用スプライトに加工する。

実写のまま丸く切り抜き、白い縁取りとツヤのハイライトだけを足す。
イラスト化や体型の変形は行わない。

使い方:
    python3 Tools/photo_to_puyo.py 入力.png 出力.png [--size 512] [--border 6]
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SUPERSAMPLE = 4          # いったん4倍で描いて縮小し、輪郭を滑らかにする
NEAR_SQUARE = 0.15       # この範囲の縦横比なら「ほぼ正方形」として中央クロップ


def find_face_center(img, side=None):
    """肌色が最も密集している場所を顔の中心とみなす。

    顔検出器（Haarカスケード）が使えない環境向けの簡易推定。
    顔は画像の上寄りにあることが多いので、上部を優先して重み付けする。
    """
    arr = np.asarray(img.convert("RGB")).astype(np.float64)
    r, g, b = arr[..., 0], arr[..., 1], arr[..., 2]

    # YCrCb に変換して肌色の範囲を取る
    y = 0.299 * r + 0.587 * g + 0.114 * b
    cr = (r - y) * 0.713 + 128
    cb = (b - y) * 0.564 + 128
    skin = (cr > 133) & (cr < 180) & (cb > 77) & (cb < 127) & (y > 60)

    if skin.sum() < arr.shape[0] * arr.shape[1] * 0.005:
        return None                      # 肌色がほとんど無ければ推定を諦める

    h, w = skin.shape
    try:
        from scipy import ndimage
        window = max(9, int(min(w, h) * 0.25) | 1)
        density = ndimage.uniform_filter(skin.astype(np.float64), size=window)
    except ImportError:
        density = skin.astype(np.float64)

    weight = np.linspace(1.10, 0.80, h)[:, None]     # 顔は上寄りにあることが多い

    # 正方形が画像からはみ出さない範囲だけを探す。
    # 端が最大になってもクランプで潰れるだけなので、最初から候補から外す。
    score = density * weight
    if side:
        half = side // 2
        # その軸に動かせる余地が無い場合は、中心1本だけを候補にする
        def span(total):
            lo, hi = half, total - half
            return (lo, lo + 1) if hi <= lo else (lo, hi)

        y0, y1 = span(h)
        x0, x1 = span(w)
        valid = np.zeros_like(score, dtype=bool)
        valid[y0:y1, x0:x1] = True
        if valid.any():
            score = np.where(valid, score, -1.0)

    cy, cx = np.unravel_index(np.argmax(score), score.shape)
    return int(cx), int(cy)


def square_crop(img):
    """正方形に切り出す。顔がなるべく中央に来るようにする。"""
    w, h = img.size
    side = min(w, h)

    if abs(w / h - 1.0) <= NEAR_SQUARE:
        cx, cy = w // 2, h // 2          # すでに正方形に近いので中央でよい
        note = "ほぼ正方形のため中央クロップ"
    else:
        center = find_face_center(img, side)
        if center is None:
            cx, cy = w // 2, h // 2
            note = "顔を推定できず中央クロップ"
        else:
            cx, cy = center
            note = f"顔の推定位置 ({cx},{cy}) を中心にクロップ"

    left = int(np.clip(cx - side // 2, 0, w - side))
    top = int(np.clip(cy - side // 2, 0, h - side))
    return img.crop((left, top, left + side, top + side)), note


def circle_alpha(size, radius, center=None):
    """アンチエイリアスされた円のアルファを作る。"""
    if center is None:
        center = (size / 2, size / 2)
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    d.ellipse([center[0] - radius, center[1] - radius,
               center[0] + radius, center[1] + radius], fill=255)
    return np.asarray(m).astype(np.float64) / 255.0


def build(img, size, border, highlight_alpha, border_rgb=(255, 255, 255)):
    cropped, note = square_crop(img)
    big = size * SUPERSAMPLE

    photo = np.asarray(cropped.convert("RGB").resize((big, big), Image.LANCZOS)).astype(np.float64)

    outer_r = big / 2.0
    inner_r = outer_r - border * SUPERSAMPLE
    a_outer = circle_alpha(big, outer_r)
    a_inner = circle_alpha(big, inner_r)
    a_ring = np.clip(a_outer - a_inner, 0.0, 1.0)      # 白い縁取りの部分

    # 写真と縁を、それぞれの被覆率で混ぜる（境界も滑らかになる）
    border_color = np.array(border_rgb, dtype=np.float64)
    total = np.clip(a_inner + a_ring, 1e-6, None)
    rgb = (photo * a_inner[..., None]
           + border_color[None, None, :] * a_ring[..., None]) / total[..., None]

    # 左上のツヤ。ぼかした楕円を白で薄く重ねる
    hl = Image.new("L", (big, big), 0)
    ImageDraw.Draw(hl).ellipse([big * 0.20, big * 0.14, big * 0.52, big * 0.36], fill=255)
    hl = hl.filter(ImageFilter.GaussianBlur(big * 0.030))
    hl_a = (np.asarray(hl).astype(np.float64) / 255.0) * highlight_alpha * a_inner
    rgb = rgb * (1.0 - hl_a[..., None]) + 255.0 * hl_a[..., None]

    rgba = np.concatenate([np.clip(rgb, 0, 255), (a_outer * 255)[..., None]], axis=-1)
    out = Image.fromarray(rgba.astype(np.uint8))
    return out.resize((size, size), Image.LANCZOS), note


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("input")
    ap.add_argument("output")
    ap.add_argument("--size", type=int, default=512, help="出力の一辺（px）")
    ap.add_argument("--border", type=int, default=6, help="白い縁取りの太さ（出力px基準）")
    ap.add_argument("--highlight", type=float, default=0.55, help="ツヤの強さ 0-1")
    ap.add_argument("--border-color", default="FFFFFF",
                    help="縁取りの色（16進 RRGGBB）。既定は白")
    args = ap.parse_args()

    if not os.path.exists(args.input):
        print(f"入力が見つかりません: {args.input}", file=sys.stderr)
        return 1

    img = Image.open(args.input)
    src_size = img.size
    hexcode = args.border_color.lstrip("#")
    if len(hexcode) != 6:
        print(f"--border-color は RRGGBB 形式で指定してください: {args.border_color}", file=sys.stderr)
        return 1
    border_rgb = tuple(int(hexcode[i:i + 2], 16) for i in (0, 2, 4))

    out, note = build(img, args.size, args.border, args.highlight, border_rgb)

    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    out.save(args.output)

    a = np.asarray(out)[..., 3]
    print(f"{os.path.basename(args.input)} {src_size} -> {os.path.basename(args.output)} {out.size}"
          f"  ({note})  透明{(a == 0).mean() * 100:.1f}% 縁の半透明{((a > 0) & (a < 255)).mean() * 100:.1f}%")
    return 0


if __name__ == "__main__":
    sys.exit(main())
