# Virtual pad that stays plugged in for a while and taps a button every 2.5 seconds.
#   pad_tap.py <seconds> <A|B|X|Y>
import sys
import time

import vgamepad as vg

BUTTONS = {
    "A": vg.XUSB_BUTTON.XUSB_GAMEPAD_A, "B": vg.XUSB_BUTTON.XUSB_GAMEPAD_B,
    "X": vg.XUSB_BUTTON.XUSB_GAMEPAD_X, "Y": vg.XUSB_BUTTON.XUSB_GAMEPAD_Y,
}

seconds = float(sys.argv[1])
button = BUTTONS[sys.argv[2].upper()]
pad = vg.VX360Gamepad()
pad.update()
time.sleep(1.5)
end = time.time() + seconds
while time.time() < end:
    pad.press_button(button)
    pad.update()
    time.sleep(0.15)
    pad.release_button(button)
    pad.update()
    time.sleep(2.35)
pad.reset()
pad.update()
time.sleep(25)  # stays connected, so the game keeps showing controller glyphs for the screenshots
del pad
