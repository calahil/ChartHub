ChartHub.Server Virtual Controller
Emulate the Directional Pad
Emulate the Select and Start
Emulate the X Y A B buttons
The controller will be controlled by SSE Endpoints or Websockets or Bluetooth or whichever will be optimal even if it is not my idea.

The Android Client will be the only consumer of these endpoints.
The RemoteControllerView should force the phone into landscape mode and have a virtual controller with the same buttons endpoints mapped to the View.
There are art assets inside the Resources/Images. We should shade them based on our colorscheme and theme. They should also react visually when we touch them.