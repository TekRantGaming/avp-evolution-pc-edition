# Virtual Xbox 360 pad for controller tests.
#   pad.py <seconds> [lx,ly] [rx,ry] [button names...]
# Stick values are -1..1. Buttons: A B X Y LB RB L3 R3 BACK START UP DOWN LEFT RIGHT, plus LT and RT for full triggers.
# The pad is plugged in, held for a second so the game can see it, driven, centred, then removed.
import sys
import time

import vgamepad as vg

B = vg.XUSB_BUTTON
NAMES = {
    "A": B.XUSB_GAMEPAD_A, "B": B.XUSB_GAMEPAD_B, "X": B.XUSB_GAMEPAD_X, "Y": B.XUSB_GAMEPAD_Y,
    "LB": B.XUSB_GAMEPAD_LEFT_SHOULDER, "RB": B.XUSB_GAMEPAD_RIGHT_SHOULDER,
    "L3": B.XUSB_GAMEPAD_LEFT_THUMB, "R3": B.XUSB_GAMEPAD_RIGHT_THUMB,
    "BACK": B.XUSB_GAMEPAD_BACK, "START": B.XUSB_GAMEPAD_START,
    "UP": B.XUSB_GAMEPAD_DPAD_UP, "DOWN": B.XUSB_GAMEPAD_DPAD_DOWN,
    "LEFT": B.XUSB_GAMEPAD_DPAD_LEFT, "RIGHT": B.XUSB_GAMEPAD_DPAD_RIGHT,
}

seconds = float(sys.argv[1])
left = tuple(float(v) for v in sys.argv[2].split(",")) if len(sys.argv) > 2 else (0, 0)
right = tuple(float(v) for v in sys.argv[3].split(",")) if len(sys.argv) > 3 else (0, 0)
buttons = [b.upper() for b in sys.argv[4:]]

pad = vg.VX360Gamepad()
pad.update()
time.sleep(float(1.5))
pad.left_joystick_float(*left)
pad.right_joystick_float(*right)
for b in buttons:
    if b == "LT":
        pad.left_trigger(255)
    elif b == "RT":
        pad.right_trigger(255)
    else:
        pad.press_button(NAMES[b])
pad.update()
time.sleep(seconds)
pad.reset()
pad.update()
time.sleep(0.3)
del pad
