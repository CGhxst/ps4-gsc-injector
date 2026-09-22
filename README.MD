# PS4 GSC Injector

Compile and inject GSC scripts for supported games on PS4.

## Supported targets

- Black Ops 2 1.10: inject PS4-compatible compiled T6 scripts in Multiplayer and Zombies through automatic script lookup.
- Black Ops 3 1.26 and 1.33: compile and inject T7 PS4 `.gscc` scripts.
- Black Ops 4 1.26: compile T8 projects and inject `.gscc` scripts through automatic Zombies, Multiplayer, and Frontend/Common hooks.

The bundled GoldHEN ps4debug v1.1.19 payload supports PS4 system software 5.05, 5.07, 6.71, 6.72, 7.00, 7.01, 7.02, 7.50, 7.51, 7.55, 8.00, 8.01, 8.03, 8.50, 8.52, 9.00, 9.03, 9.04, 9.50, 9.51, 9.60, 10.00, 10.01, 10.50, 10.51, 10.70, 10.71, 11.00, 11.02, 11.50, 11.52, 12.00, and 12.02.

## Requirements

- Windows with .NET Framework 4.8.
- A jailbroken PS4 running one of the listed game versions.
- The PS4 and computer connected to the same network.
- A PS4 bin loader for sending the bundled payload.

## Compile

Compilation is available for Black Ops 3 and Black Ops 4. Black Ops 2 accepts existing PS4-compatible compiled T6 scripts.

Compiled files must match the game's VM and platform. A PC or PS3 menu is not automatically compatible with PS4. The bundled compilers do not implement every extension used by other compilers or custom loaders.

1. Select the game and its version or mode.
2. Select the root folder of the GSC project.
3. Choose the output path for the compiled `.gscc` file.
4. Select **Compile Project**.

## Inject

1. Enable the PS4 jailbreak and open its bin loader.
2. Enter the PS4 IP address and payload port, then select **Send Payload**.
3. Select the game and its version or mode in the app, then load that mode on the PS4.
4. Select the matching attach button. Attach again after restarting the game or switching BO2 modes.
5. Choose the matching compiled `.gsc` or `.gscc` file and select **Inject Script**. For BO2, inject at the Multiplayer or Zombies menu before starting a match.

Black Ops 2 scripts must be compiled for the PS4 T6 VM and embed the hook path for the selected mode:

- Multiplayer: `maps/mp/gametypes/_clientids.gsc`
- Zombies: `maps/mp/gametypes_zm/_clientids.gsc`

Injection replaces a script asset; it does not restart script threads already running. Follow the menu's loading instructions. Previously injected memory is retained until the game exits because running threads may still reference it. Restart the game to unload injected scripts, or after a connection failure during injection.

BO4 also adds an include to the selected mode's hook when needed. If that script has no verified free space for the include, injection is refused to avoid overwriting another section.

## Verification

Build `PS4GSCInjector.sln` in Release using Visual Studio with the .NET Framework 4.8 targeting pack, then run `PS4GSCInjector.Tests` with the Visual Studio test runner. Tests cover compiler output and limits, malformed script layouts, TCP packet handling, failed-write rollback, and all game profiles against a local ps4debug protocol emulator.

Protocol and compilation tests do not run the game's VM. BO2 Multiplayer and Zombies have been tested on a PS4 during development; the latest shared injection changes and BO3/BO4 still require live game testing. Compatibility with every menu or game region is not guaranteed.

## Credits

- cghxst: firmware payload integration, UI work, and release packaging.
- shiversoftdev and ate47: T7 and T8 compiler foundations, research, and injection concepts.
- ItsJokerZz and xensik: T6 PS4 format, compiler, and loader research.
