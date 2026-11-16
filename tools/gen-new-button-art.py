"""Generate artwork for the On Foot / SRV / Colonisation static buttons and the info boards.

Matches the RobManders toggle set the plugin already uses: a word or two on black, with a
rounded tab along the bottom - pale blue-green for ON, amber for OFF. Info boards get a dark
plate with a coloured header strip for Active / Inactive / Alert.

    python tools/gen-new-button-art.py

Writes into Elite/Images/Optional/Generated. Re-run after changing the lists below.
"""
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'Elite', 'Images', 'Optional', 'Generated')

SIZE = 256
ON = (168, 216, 216)       # RobManders ON tab
OFF = (204, 122, 0)        # RobManders OFF tab
ALERT = (214, 48, 32)
TEXT_ON = ON
TEXT_OFF = OFF

FONT = 'C:/Windows/Fonts/bahnschrift.ttf'
FONT_BOLD = 'C:/Windows/Fonts/arialbd.ttf'

# file stem -> (label lines, on word, off word)
STATICS = {
    # On foot
    'OnFoot-Pulse-Scan': (['PULSE', 'SCAN'], 'READY', 'N/A'),
    'OnFoot-Energy-Cell': (['ENERGY', 'CELL'], 'READY', 'N/A'),
    'OnFoot-Flashlight': (['FLASH', 'LIGHT'], 'ON', 'OFF'),
    'OnFoot-Shield': (['SUIT', 'SHIELD'], 'ON', 'OFF'),
    # SRV
    'SRV-Target-Ahead': (['TARGET', 'AHEAD'], 'READY', 'N/A'),
    'SRV-Eject-Cargo': (['EJECT', 'CARGO'], 'CARGO', 'EMPTY'),
    'SRV-Cargo-Scoop': (['CARGO', 'SCOOP'], 'DEPL', 'RETR'),
    'SRV-Headlights': (['HEAD', 'LIGHTS'], 'ON', 'OFF'),
    # Colonisation
    'Col-Trigger-Module': (['COLONY', 'MODULE'], 'READY', 'N/A'),
    'Col-Place-Settlement': (['PLACE', 'SETTLEMENT'], 'ACTIVE', 'IDLE'),
    'Col-Change-Option': (['NEXT', 'OPTION'], 'ACTIVE', 'IDLE'),
    'Col-Rotate-Left': (['ROTATE', 'LEFT'], 'ACTIVE', 'IDLE'),
    'Col-Rotate-Right': (['ROTATE', 'RIGHT'], 'ACTIVE', 'IDLE'),
    'Col-Exit-Camera': (['EXIT', 'CAMERA'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Speed-Up': (['CAM', 'SPEED +'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Speed-Down': (['CAM', 'SPEED -'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Forward': (['CAM', 'FORWARD'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Back': (['CAM', 'BACK'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Left': (['CAM', 'LEFT'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Right': (['CAM', 'RIGHT'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Up': (['CAM', 'UP'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Down': (['CAM', 'DOWN'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Pitch-Up': (['CAM', 'PITCH UP'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Pitch-Down': (['CAM', 'PITCH DN'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Yaw-Left': (['CAM', 'YAW LEFT'], 'ACTIVE', 'IDLE'),
    'Col-Cam-Yaw-Right': (['CAM', 'YAW RIGHT'], 'ACTIVE', 'IDLE'),
}

BOARDS = ['Board-Active', 'Board-Inactive', 'Board-Alert']


def font(path, size):
    return ImageFont.truetype(path, size)


def fit(draw, text, path, start, max_w):
    size = start
    while size > 10:
        f = font(path, size)
        if draw.textlength(text, font=f) <= max_w:
            return f
        size -= 2
    return font(path, size)


def static_image(lines, word, colour, text_colour):
    img = Image.new('RGB', (SIZE, SIZE), (0, 0, 0))
    d = ImageDraw.Draw(img)

    # Label, centred in the top 170px.
    fonts = [fit(d, l, FONT, 60, SIZE - 24) for l in lines]
    size = min(f.size for f in fonts)
    f = font(FONT, size)
    heights = [d.textbbox((0, 0), l, font=f)[3] for l in lines]
    gap = 10
    total = sum(heights) + gap * (len(lines) - 1)
    y = (170 - total) / 2 + 6
    for l, h in zip(lines, heights):
        w = d.textlength(l, font=f)
        d.text(((SIZE - w) / 2, y), l, font=f, fill=text_colour)
        y += h + gap

    # Rounded tab along the bottom with the state word.
    d.rounded_rectangle((6, 174, SIZE - 6, SIZE + 40), radius=34, fill=colour)
    wf = fit(d, word, FONT_BOLD, 52, SIZE - 40)
    bbox = d.textbbox((0, 0), word, font=wf)
    d.text(((SIZE - (bbox[2] - bbox[0])) / 2 - bbox[0], 174 + (82 - (bbox[3] - bbox[1])) / 2 - bbox[1]),
           word, font=wf, fill=(0, 0, 0))
    return img


def board_image(colour, dim):
    img = Image.new('RGB', (SIZE, SIZE), (0, 0, 0))
    d = ImageDraw.Draw(img)
    base = tuple(int(c * (0.10 if dim else 0.16)) for c in colour)
    d.rounded_rectangle((4, 4, SIZE - 4, SIZE - 4), radius=22, fill=base,
                        outline=tuple(int(c * (0.45 if dim else 0.9)) for c in colour), width=5)
    d.rectangle((24, 14, SIZE - 24, 20), fill=tuple(int(c * (0.45 if dim else 1)) for c in colour))
    return img


def main():
    os.makedirs(OUT, exist_ok=True)

    for stem, (lines, on_word, off_word) in STATICS.items():
        static_image(lines, on_word, ON, TEXT_ON).save(os.path.join(OUT, stem + '_ON.png'))
        static_image(lines, off_word, OFF, TEXT_OFF).save(os.path.join(OUT, stem + '_OFF.png'))

    board_image(ON, False).save(os.path.join(OUT, 'Board-Active.png'))
    board_image((140, 140, 140), True).save(os.path.join(OUT, 'Board-Inactive.png'))
    board_image(ALERT, False).save(os.path.join(OUT, 'Board-Alert.png'))

    print('wrote', len(STATICS) * 2 + 3, 'images to', OUT)


if __name__ == '__main__':
    main()
