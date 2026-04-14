## 3 new Input features for the Android client and ChartHub.Server

While designing these new features follw all repow wide agent governance.

---------------------------------------------------------------------------------------------------

## ChartHub.Server Components

## Auth
-Requires the same auth level to do anything else on the server.

## Virtual Controller
-Emulate the Directional Pad
-Emulate the Select and Start
-Emulate the X Y A B buttons
-The controller will be controlled by SSE Endpoints or Websockets or Bluetooth or whichever will be optimal even if it is not my idea.

## Virtual Touch Pad Mouse
-Emulate Mouse/touchpad
-Emulate Right and Left Mouse Buttons
-TouchPad uses either SSE Endpoints or Websockets or Bluetooth or whichever will be optimal even if it is not my idea.
-There are art assets in Resources/Images for the mouse buttons.

## Virtual Keyboard
-Emulate a Keyboard so we can input into text input on the emubox console
-Keyboard uses either SSE Endpoints or Websockets or Bluetooth or whichever will be optimal even if it is not my idea.

---------------------------------------------------------------------------------------------------

## The Android Client Componets

## Menu Item
-Named Input
-should expand to display Controller, Mouse, Keyboard

## Virtual Controller Frontend
-The VirtualControllerView should force the phone into landscape mode and have a virtual controller with the same buttons endpoints mapped to the View.
-There are art assets inside the Resources/Images. We should shade them based on our colorscheme and theme. They should also react visually and haptic feedback when we touch them.

## Virtual TouchPad Frontend
-VirtualTouchPadView should force the phone into landscape mode and have a touchpad square on the left of the screen and the right and left mouse buttons on the right side of the screen
-I am open to other ways to implement this feature and welcome clarifying questions.

## Virtual Keyboard Frontend
-If possible VirtualKeyboardView should show the android keyboard and send the keystrokes to the endpoint
-I am also open to other ways to implement this feature and welcome clarifying questions.

---------------------------------------------------------------------------------------------------

