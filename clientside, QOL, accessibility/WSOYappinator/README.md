# WSO Yappinator

Get a variety of characters to yap at you while you fly!

## Unzip the downloaded files in `BepInEx/plugins`
* [Download mod + voicepacks](1.0.8/WSOYappinator_1.0.8.dll)
* [Download extra voicepacks](https://github.com/nikkorap/NuclearMods/raw/refs/heads/main/clientside,%20QOL,%20accessibility/WSOYappinator/WSOYappinator_1.0.8_FULL_PACK_Part2.7z)
* [Browse all published voicepacks](https://github.com/nikkorap/NuclearMods/tree/main/clientside%2C%20QOL%2C%20accessibility/WSOYappinator/1.0.8/audio)

Folder structure should look something like this:
```
bepinex/plugins/
  └─ WSOYappinator/
    ├─ WSOYappinator_version.dll
    └─ audio/
       ├─ Prez/
       │  ├─ eventPriorities.txt
       │  └─ audiofiles.wav
       └─ Galaxy/
          ├─ eventPriorities.txt
          └─ audiofiles.wav
```
## How to make new Packs

you can add your own voicelines by dropping them into the mod's audio/name folder, or make a whole separate voiceline set by making a new folder with .wav files and a eventPriorities.txt file.
The files have to be .wav and have a name like this: (event)_(number).wav.

these are the current events and their priority levels, you can customise them in the voiceline sets eventPriorities.txt.
```
jump=6
death=5
damage=4
rwr=3
kill=2
hit=2
nuclear=2
bomb=2
missile=2
fox2=2
fox3=2
guns=2
spawn=1
```
