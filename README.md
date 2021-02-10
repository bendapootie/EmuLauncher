# EmuLauncher

https://github.com/bendapootie/EmuLauncher

Program to help launch games in an emulator



# How to Use

## Motivation (aka. why did I write this)

I was setting up a TV-PC that could be controlled with only a gamepad. Steam's Big Picture Mode works well for most PC games, but the emulation ecosystem isn't as friendly. For one, launching emulated games via Steam isn't straightforward, but there's the added problem that many emulators don't have a way to close the emulator from the gamepad.

Emu Launcher is the result of me trying to solve the problems I was running into.



## Example

My goal is to be able to launch Wii U emulated games on my TV by just using a gamepad. But, I'd also like the roms and save states for the emulated games to stay in sync between my TV PC and my desktop machine.

Here's the setup I'm using to get that to work...

1. TV PC is configured to launch directly into [Steam Big Picture Mode](https://store.steampowered.com/bigpicture)
2. [Attract-Mode](http://attractmode.org/) launcher is added as launchable from Steam
   - Attract-Mode is a gamepad-friendly front-end for browsing and launching emulators
   - Problem - Attract-Mode expects files (ie. roms) to be laid out in a certain way that doesn't align with how I have [Cemu](https://cemu.info/) (Wii U emulator) set up
3. Generate Launch Scripts
   - In a sibling folder to your roms, create a "LaunchScripts" folder
   - Create a ".emu" script for each rom
4. Set Cemu to run emu_launcher.exe to run the game with the .emu script as the command line parameter

## Sample Scripts

The script files can named anything you want, but I've been using ".emu"

### Simple Example

This simple script will launch Cemu in full screen with the Super Mario 3d World rom 

``` emu
launch_exe = c:\games\emulation\cemu_1.22.4\Cemu.exe
launch_args = -g "c:\games\emulation\roms\Super Mario 3D World\pwr-sm3dw.wux" -f
```

### Example with Variables

Emu Launcher supports basic variable replacement similar to batch file variables

Variables can be pretty much anything you want, but there are a few reserved words

``` emu
root = c:\games\emulation
rom = Super Mario 3D World\pwr-sm3dw.wux
launch_exe = $root$\cemu_1.22.4\Cemu.exe
launch_args = -g "$root$\roms\$rom$" -f
```

### Example with Includes

"include" is a special variable that will parse the "included" file. The main reason I implemented it was so I could use the predefined variable `$MachineName$` to have the same scripts behave differently on different machines.

eg. My emulation folder is synced between my desktop and TV-PC so they can share roms and save games, but since they have different video cards, they need to run separate copies of Cemu so they don't pollute each other's shader cache.

``` emu
// desktop_machine.emu_inc
// Note: Lines that start with '//' are considered comments and ignored by Emu Launcher
// Also, I changed the extension of the file I want to include so that Cemu won't
// mistakenly think it should be an entry in its game list
root = C:\games\emulation\emulation\WiiU
```

``` emu
// TV_PC.emu_inc
// Note: The 'games' folder on the TV_PC is on a different drive
root = E:\games\emulation\emulation\WiiU
```

``` emu
// Super Mario 3D World.emu
// Note: This assumes my desktop machine is named "desktop_machine" and my TV machine is "TV_PC"
include = $MachineName$.emu_inc
rom = Super Mario 3D World\pwr-sm3dw.wux
launch_exe = $root$\cemu_1.22.4\Cemu.exe
launch_args = -g "$root$\roms\$rom$" -f
```



## TODO

Despite Attract-Mode having a setting to allow a controller input to close the emulator, I haven't been able to get it to work, so I'll likely try to add this feature to EmuLauncher.



# Building

* Built using Visual Studio Community 2019 (v 16.8.4) with .net 5.0
  * There's nothing fancy in the code so it should work fine with older versions of .net
  * It does use Windows.Forms for the UI, so that does limit it to Windows.
    It shouldn't be too hard to make a command line version if needed



# Licenses

Emu picture

* Derived from image in the [Public Domain](https://search.creativecommons.org/photos/0a57c39a-ee67-47f8-a575-122ef4da62a0)
* Photographer - [Mathias Appel](https://www.flickr.com/photos/mathiasappel/)


