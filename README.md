# NuclearMods
My BepInEx mods for Nuclear Option

How to install BepInEx (5 mono) guide [https://docs.bepinex.dev/articles/user_guide/installation/index.html]

tldr:
1. Download the correct version of BepInEx [https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x64_5.4.23.3.zip]
2. Extract the contents into the game root (where [NuclearOption.exe] lives)
3. Start the game once to generate configuration files.
4. Open [Nuclear Option\BepInEx\config\BepInEx.cfg] and make sure that the setting [HideGameManagerObject] is set to to [true].

how to install mods for BepInEx?
- For your first mod i suggest downloading this mod: [Mod Configuration manager]
   [https://github.com/BepInEx/BepInEx.ConfigurationManager/releases/download/v18.4/BepInEx.ConfigurationManager_BepInEx5_v18.4.zip]
   This is an awesome plugin that gives you an ingame mod menu to control mods (i try to add support for it in my mods) [TOGGLE WITH F1]
- in the downloaded zip file there is a BepInEx folder, you can drop it right in the install folder.
- Some other mods might be just .dll files, drop those in the [Nuclear Option\BepInEx\plugins] folder
