## WSO Yappinator

Get a variety of characters to yap at you while you fly!

Currently features:
* Prez from Project Wingman
* Galaxy from Project Wingman
* Pixy from Ace Combat (pack by Sneezy)
* AWACS Sky Eye from Ace Combat (pack by Sneezy)

Folder structure should look something like this:

`bepinex/plugins/
  └─ WSOYappinator/
    ├─ WSOYappinator_version.dll
    └─ audio/
       ├─ Prez/
       │  ├─ eventPriorities.txt
       │  └─ audiofiles.wav
       └─ Galaxy/
          ├─ eventPriorities.txt
          └─ audiofiles.wav`

you can easily add your own voicelines by dropping them into the mod's audio/name folder, or make a whole separate voiceline set by making a new folder with .wav files and a eventPriorities.txt file.
The files have to be .wav and have a name like this: (event)_(number).wav 
these are the current events and their priority levels, you can customise them in the voiceline sets eventPriorities.txt.

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
