# NuclearMods [For Nuclear Option 0.30]
My BepInEx mods for Nuclear Option, feel free to DM me on discord if you have any questions or ideas.

# How to install BepInEx (5 mono) guide [https://docs.bepinex.dev/articles/user_guide/installation/index.html]

TLDR:
1. Download the correct version of BepInEx (bepinex 5 mono) [https://github.com/BepInEx/BepInEx]
2. Extract the contents into the game root (where [NuclearOption.exe] lives)
3. Start the game once to generate configuration files.
4. Open [Nuclear Option\BepInEx\config\BepInEx.cfg] and make sure that the setting [Chainloader]
 -> HideGameManagerObject = true.
5. (optional) also edit [Logging.Console]-> Enabled = true.

(you can also change bepinex settings ingame using the Mod Configuration manager)


## How to install mods for BepInEx?
- For your first mod download this mod: [Mod Configuration manager][https://github.com/BepInEx/BepInEx.ConfigurationManager]
- This is an awesome plugin that gives you an ingame mod menu to control mods (i try to add support for it in my mods) [TOGGLE WITH F1]

  ![ModMenuDemo](https://github.com/user-attachments/assets/6ee561c4-f2ac-4798-9896-a3dc8bca9714)
  
- in the downloaded zip file there is a BepInEx folder, you can drop it right in the install folder.
- Some other mods might be just .dll files, drop those in [Nuclear Option\BepInEx\plugins\ (optional folder)]

# My Mods
these mods might be kinda scuffed so i give no guarantees about their functionality or stuff not breaking.

## Only use in private lobbies with everyone's consent.
These won't work in lobbies where the host doesn't have the mod active. Don't even try, if i find out i'm going to [REDACTED].


# Clientside / Quality of life
## DetailedClimbRate
Is seaskimming too ambigious? then grab this mod to turn 0m/s into 0.00m/s (or feet if you're into that)

![DetailedClimbrateReadingDemo](https://github.com/user-attachments/assets/01976472-8195-40bd-b43a-3ad231a3c60f)

## Custom Default Loadouts
Set your preferred Livery, weapons, and fuel level as the new defaults! 

## Scrolling weapon selection
Cant be bothered to click each hardpoint and weapon each time? just hover over the button and scroll your wheel to change the weapon!

## RandomLoadouts_client
Want to be more 𝓡𝓪𝓷𝓭𝓸𝓶? public matches too repetitive? Then let the dice choose your next loadout! (should work with unrestricted weapons too if the server supports it)

## WSO Yappinator
Get totally-not-Prez to yap at you while you fly


# HostSide
Only the host needs these mods for them to work for everyone

## Spawnertest
Basically zeus from arma 3

## OneLifeTest
All player pilots have one chance, if they crash they get to fly an unarmed (or customisable limited loadout) Chicane or Tarantula until they successfully bring a friendly pilot back to an airbase.

## DetectionDisable
You can completely disable Visual detection or Radar detection, or both! And only rely on the good old MK1 Eyeball

## RandomLoadouts_Host
Hosting a game and want to add some excitement to your players? Make it unfair for everyone equally and force everyone to roll the dice every time they step into the cockpit! (afaik others can be completely modless peasants! though mods are still recommended) (should work with unrestricted weapons too)


# Other
All players need these mods for them to function correctly

## 155mm_Railgun
replaces the Ifrit's 27mm Autocannon with the Dynamos 155mm Railgun, also adds it to every hardpoint.

![railgundemo](https://github.com/user-attachments/assets/dfc22fc2-e855-4fea-88db-269287ffe446)

## RubberBullets
Targets dying too quick? want to prank your teammates with fake missiles? want to unscrew the detonator from your bombs and see if you can get someone with pure kinetic energy? then this mod is for you!

## UnrestrictedWeapons
Want more guns? More Bombs? whatever you want, you can have it, if your airframe can handle it. 

![unrestrictedweaponsdemo](https://github.com/user-attachments/assets/9d16515a-b27e-448f-b571-c3d476d4c337)

## namegrabber
not intended for gameplay, i made this to help missionmakers. it creates a .txt file with unit's internal names and ingame names, like this: ([Aircraft][AttackHelo1][SAH-46 Chicane][The SAH-46 Chicane retains a traditional attack helicopter configuation while incorporating upgrades that keep it a formidable presence on the battlefield of the late 21st Century. Cutting edge stealth technology allows the Chicane to conduct pop-up attacks from behind terrain and disappear from radar without a trace.])

# TODO
- figure out networking black magic and try syncing config settings between host and clients.
- Loadout preset mod? -done'ish
- custom waypoint/marker?
- more mods?
