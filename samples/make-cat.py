"""Render KIRIHARI's original, three-minute editing sample."""

import argparse
import math
from pathlib import Path
import subprocess

from PIL import Image, ImageDraw, ImageFont


SIZE = (640, 360)
FPS = 24
DURATION = 180
GROUND = 276
SCENES = (
    ("01  RUN", "#c7ece4", "#729f8c", "#f4faef", "#88bca8"),
    ("02  JUMP", "#d8e0fb", "#8a98ca", "#fbf4ff", "#a7b1e2"),
    ("03  CHASE", "#f8debd", "#ce9879", "#fff4de", "#e8af87"),
)
INK = "#27354b"
CAT = "#ffb452"
DARK_FUR = "#bb632d"


def font(size):
    return ImageFont.truetype("segoeuib.ttf", size)


TITLE, CLOCK, DETAIL = font(21), font(21), font(13)


def stage(scene):
    name, sky, floor, paper, hill = SCENES[scene]
    image = Image.new("RGB", SIZE, sky)
    draw = ImageDraw.Draw(image)
    draw.ellipse((496, 43, 568, 115), fill=paper)
    draw.ellipse((-110, 157, 355, 358), fill=hill)
    draw.ellipse((237, 144, 766, 378), fill=hill)
    for x, y in ((118, 146), (237, 138), (377, 168), (560, 157)):
        draw.ellipse((x - 22, y - 9, x + 22, y + 9), fill=paper)
        draw.ellipse((x - 11, y - 15, x + 10, y + 9), fill=paper)
    draw.rectangle((0, GROUND, SIZE[0], SIZE[1]), fill=floor)
    draw.line((0, GROUND, SIZE[0], GROUND), fill=INK, width=3)
    for x in range(34, SIZE[0], 70):
        draw.line((x, 292, x + 26, 292), fill=paper, width=2)
    draw.rounded_rectangle((24, 22, 210, 77), 10, fill=INK)
    draw.text((37, 26), name, fill=paper, font=TITLE)
    draw.text((37, 54), "KIRIHARI MOTION TEST", fill=paper, font=DETAIL)
    return image


STAGES = tuple(stage(scene) for scene in range(3))


def draw_cat(draw, x, y, direction, phase):
    def point(px, py):
        return (round(x + direction * px), round(y + py))

    def ellipse(left, top, right, bottom, fill, outline=None, width=1):
        a, b = point(left, top), point(right, bottom)
        draw.ellipse((min(a[0], b[0]), a[1], max(a[0], b[0]), b[1]),
                     fill=fill, outline=outline, width=width)

    def polygon(points, fill, outline=None):
        draw.polygon([point(*p) for p in points], fill=fill, outline=outline)

    def line(points, fill, width):
        draw.line([point(*p) for p in points], fill=fill, width=width, joint="curve")

    tail = [(-31 - 38 * u, -5 - 32 * math.sin(u * math.pi / 2) + u * 7 * math.sin(phase / 2))
            for u in (i / 16 for i in range(17))]
    line(tail, INK, 13)
    line(tail, CAT, 8)
    for leg, offset, fill in ((-26, math.pi, DARK_FUR), (24, 0, DARK_FUR),
                              (-20, 0, CAT), (29, math.pi, CAT)):
        swing = math.sin(phase + offset)
        knee = (leg + 8 * swing, 19)
        paw = (leg + 17 * swing, 27 - max(0, math.cos(phase + offset)) * 11)
        line(((leg, 9), knee, paw), INK, 10)
        line(((leg, 9), knee, paw), fill, 6)
        ellipse(paw[0] - 5, paw[1] - 3, paw[0] + 6, paw[1] + 3, fill, INK)
    ellipse(-40, -24, 42, 18, CAT, INK, 3)
    for stripe in (-19, -6, 7):
        polygon(((stripe, -22), (stripe + 7, -23), (stripe + 3, -8)), DARK_FUR)
    polygon(((29, -34), (30, -58), (47, -45), (61, -58), (65, -28)), CAT, INK)
    polygon(((33, -51), (35, -36), (43, -43)), "#f58b83")
    polygon(((58, -50), (53, -40), (63, -37)), "#f58b83")
    ellipse(26, -45, 72, -4, CAT, INK, 3)
    ellipse(49, -27, 72, -7, "#fff0d0")
    ellipse(48, -33, 53, -26, INK)
    ellipse(65, -34, 69, -27, INK)
    polygon(((59, -24), (65, -24), (62, -20)), INK)
    line(((62, -20), (62, -17), (66, -15)), INK, 1)
    line(((49, -20), (39, -22)), INK, 1)
    line(((49, -17), (38, -15)), INK, 1)


def frame(index):
    t = index / FPS
    scene = int(t // 60)
    local = t % 60
    image = STAGES[scene].copy()
    draw = ImageDraw.Draw(image)
    travel = (local / (4.5 if scene == 2 else 5.5)) % 2
    direction = 1 if travel < 1 else -1
    x = 86 + 468 * (1 - abs(1 - travel))
    phase = local * (22 if scene == 2 else 17)
    jump = 76 * max(0, math.sin(local * math.pi / 1.45)) ** 1.25 if scene == 1 else 0
    bob = 3 * abs(math.sin(phase))
    draw.ellipse((x - 39 + jump / 8, GROUND + 4, x + 39 - jump / 8, GROUND + 15),
                 fill=SCENES[scene][3])
    if scene == 2:
        ball_x = min(624, max(16, x + 88 * direction))
        ball_y = GROUND - 15 - 29 * abs(math.sin(local * 5))
        draw.ellipse((ball_x - 13, ball_y - 13, ball_x + 13, ball_y + 13), fill="#6b65d4", outline=INK, width=2)
        draw.ellipse((ball_x - 5, ball_y - 7, ball_x, ball_y - 2), fill="#ece4ff")
    draw_cat(draw, x, GROUND - 29 - jump - bob, direction, phase)
    whole = index // FPS
    draw.rounded_rectangle((476, 22, 616, 77), 10, fill=INK)
    draw.text((490, 26), f"{whole // 60:02d}:{whole % 60:02d}.{index % FPS * 100 // FPS:02d}",
              fill="white", font=CLOCK)
    draw.text((491, 54), f"F {index + 1:04d}  /  {FPS} FPS", fill="#cfdaee", font=DETAIL)
    draw.rounded_rectangle((24, 317, 616, 334), 7, fill=INK)
    progress = 590 * index / (FPS * DURATION - 1)
    if progress >= 2:
        draw.rounded_rectangle((25, 318, 25 + progress, 333), 6, fill="#ffcd73")
    for x in (221, 418):
        draw.line((x, 320, x, 331), fill="white", width=2)
    return image


def insert_frame(index):
    t = index / 30
    image = Image.new("RGB", (480, 270), "#151631")
    draw = ImageDraw.Draw(image)
    for i in range(52):
        x, y = (i * 119 + 23 - t * (7 + i % 4 * 4)) % 480, (i * 91 + 47) % 270
        draw.ellipse((x, y, x + 2 + i % 2, y + 2 + i % 2), fill="#b8bbf1")
    draw.ellipse((340, 144, 534, 339), fill="#733ba5", outline="#b576e3", width=3)
    x, y = 40 + t * 57, 164 - 60 * math.sin(t * math.pi / 7)
    draw.polygon([(x - 25, y - 9), (x - 58 - 8 * math.sin(t * 29), y), (x - 25, y + 9)], fill="#ffbb57")
    draw.polygon([(x - 20, y - 11), (x - 26, y - 25), (x + 9, y - 12),
                  (x + 9, y + 12), (x - 26, y + 25), (x - 20, y + 11)], fill="#e56eae")
    draw.rounded_rectangle((x - 26, y - 12, x + 30, y + 12), 12, fill="#e9edff")
    draw.polygon([(x + 22, y - 11), (x + 43, y), (x + 22, y + 11)], fill="#f47ab7")
    draw.ellipse((x + 2, y - 7, x + 16, y + 7), fill="#53bdd8", outline=INK, width=2)
    draw.rounded_rectangle((20, 20, 220, 69), 9, fill="#292c52")
    draw.text((32, 24), "ROCKET PASS", fill="white", font=TITLE)
    draw.text((33, 50), "INSERT TEST  ·  30 FPS", fill="#b9c9fa", font=DETAIL)
    draw.text((352, 27), f"{t:05.2f} s", fill="#ffe5a5", font=CLOCK)
    return image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--insert", action="store_true", help="Render the seven-second rocket instead.")
    args = parser.parse_args()
    target = Path(__file__).with_name("insert-motion.mp4" if args.insert else "cat-motion.mp4")
    beep = "aevalsrc=exprs='0.05*sin(2*PI*640*t)*exp(-30*mod(t,4))*lt(mod(t,4),0.18)':s=48000"
    size, fps, duration = ("480x270", 30, 7) if args.insert else ("640x360", FPS, DURATION)
    render = insert_frame if args.insert else frame
    command = [
        args.ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
        "-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", size,
        "-framerate", str(fps), "-i", "pipe:0",
        *([] if args.insert else ["-f", "lavfi", "-i", beep, "-c:a", "aac", "-b:a", "48k"]),
        "-t", str(duration), "-c:v", "libx264", "-preset", "veryfast", "-crf", "25",
        "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(target),
    ]
    with subprocess.Popen(command, stdin=subprocess.PIPE) as encoder:
        try:
            for index in range(fps * duration):
                encoder.stdin.write(render(index).tobytes())
        finally:
            encoder.stdin.close()
        if encoder.wait() != 0:
            raise SystemExit("Could not encode the cat sample.")


if __name__ == "__main__":
    main()
