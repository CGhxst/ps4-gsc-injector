# PS4 GSC Injector

Compile and inject GSC scripts for BO3 and BO4 on PS4.

## Supported targets

- Black Ops 3 1.26 and 1.33: compile and inject T7 PS4 `.gscc` scripts.
- Black Ops 4 1.26: compile T8 projects and inject `.gscc` scripts through automatic Zombies, Multiplayer, and Frontend/Common hooks.

The bundled GoldHEN ps4debug v1.1.19 payload supports PS4 system software 5.05, 5.07, 6.71, 6.72, 7.00, 7.01, 7.02, 7.50, 7.51, 7.55, 8.00, 8.01, 8.03, 8.50, 8.52, 9.00, 9.03, 9.04, 9.50, 9.51, 9.60, 10.00, 10.01, 10.50, 10.51, 10.70, 10.71, 11.00, 11.02, 11.50, 11.52, 12.00, and 12.02.

## Compile

1. Select the game and its version or mode.
2. Select the root folder of the GSC project.
3. Choose the output path for the compiled `.gscc` file.
4. Select **Compile Project**.

## Inject

1. Enable the PS4 jailbreak and open its bin loader.
2. Enter the PS4 IP address and payload port, then select **Send Payload**.
3. Start the selected game. For Black Ops 4, load the mode selected in the app.
4. Select **Attach BO3** or **Attach BO4**.
5. Choose the matching compiled `.gscc` file and select **Inject Script**.

## Credits

- cghxst: firmware payload integration, UI work, and release packaging.
- shiversoftdev and ate47: T7 and T8 compiler foundations, research, and injection concepts.
