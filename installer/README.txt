================================================================================
 Elite Dangerous plugin for Stream Dock (Ajazz / Mirabox)
 with automatic profile switching
================================================================================

This package installs the Elite Dangerous Stream Deck plugin on an Ajazz / Mirabox
Stream Dock deck, and adds the piece Stream Dock is missing so that the plugin's
automatic profile switching actually works - a different button layout for flying,
driving the SRV, on foot, and in a fighter.


--------------------------------------------------------------------------------
 What it works with
--------------------------------------------------------------------------------

  Deck        : Ajazz AKP153E (15 keys + 3 screen-only displays)
  Software    : Stream Dock AJAZZ 3.10.191.0421
  Game        : Elite Dangerous (Horizons or Odyssey)
  Windows     : 10 or 11, 64-bit

The plugin itself runs on any Stream Dock deck. The automatic profile switching
part is tied to this exact Stream Dock build (see "How the switching works"),
and the installer refuses to patch any other version rather than risk it.


--------------------------------------------------------------------------------
 What you get
--------------------------------------------------------------------------------

  * The full Elite Dangerous plugin - every button, with artwork already filled
    in so buttons are usable the moment you drop them on the deck.

  * The three screen-only displays on the AKP153E can hold the info buttons
    (ship status, nav, exploration signals, and the rest).

  * Four profiles - Elite Main, Elite OnFoot, Elite InSRV, Elite InFighter -
    that the plugin switches between automatically while you play.


--------------------------------------------------------------------------------
 Installing
--------------------------------------------------------------------------------

  1. Install Stream Dock (the Ajazz app) and run it once with the deck plugged in.
  2. Install the .NET 10 Desktop Runtime if you do not have it:
       https://dotnet.microsoft.com/en-us/download/dotnet/10.0
  3. Right-click Install.bat and "Run as administrator".

That is all. The installer:

  - installs the plugin and its artwork into Stream Dock,
  - creates the four Elite profiles,
  - installs the profile bridge (sdbridge.dll) next to Stream Dock,
  - patches Stream Dock so it loads the bridge on its own from then on.

Start Stream Dock, arrange your buttons on each of the four profiles, and play.
The deck follows the game: it shows the OnFoot profile when you are on foot, the
InSRV profile in the SRV, and so on.

Administrator rights are needed because the bridge and the patch go into the
Stream Dock program folder. The installer backs the Stream Dock executable up to
"Stream Dock AJAZZ.exe.bak" before touching it.


--------------------------------------------------------------------------------
 Uninstalling
--------------------------------------------------------------------------------

Right-click Uninstall.bat and "Run as administrator". It reverts the Stream Dock
patch, removes the bridge, and removes the plugin. The four Elite profiles are
left in place in case you arranged buttons on them; delete them by hand in Stream
Dock if you want them gone.


--------------------------------------------------------------------------------
 How the profile switching works (and why the patch is needed)
--------------------------------------------------------------------------------

Stream Deck plugins ask the software to change profile by sending a
"switchToProfile" message. Elgato's own software acts on it. Stream Dock reads
the message but does nothing with it - the profile never changes. So the plugin
was already asking for the right profile as you played; Stream Dock was ignoring
the request.

The bridge (sdbridge.dll) fills that gap. It runs inside Stream Dock, watches for
the switchToProfile request, and performs the switch using Stream Dock's own
profile-change routine - the same one that runs when you click a profile in the
list. Nothing about how the deck draws or behaves is changed; only the ignored
request is carried out.

For the bridge to run without you starting anything by hand, the installer adds a
small loader to the Stream Dock executable so it loads the bridge at startup. The
original program is not modified beyond that - the loader runs, then hands off to
Stream Dock's normal startup. A backup is kept, and Uninstall.bat reverses it.

Because the loader and the bridge are tied to the exact Stream Dock build above,
a Stream Dock update will replace the patched executable with a fresh one and
switching will stop working until this is re-run against the new version. The
plugin itself keeps working; only the automatic switching depends on the patch.


--------------------------------------------------------------------------------
 Package contents
--------------------------------------------------------------------------------

  Install.bat / Install.ps1      the installer
  Uninstall.bat / Uninstall.ps1  the uninstaller
  plugin\                        the Elite Dangerous plugin and its artwork
  bridge\sdbridge.dll            the profile bridge
  bridge\Patch-StreamDock.ps1    the code-cave patcher used by the installer
  README.txt                     this file


--------------------------------------------------------------------------------
 Credits
--------------------------------------------------------------------------------

Plugin: mhwlng, DrFr33ze, macrossmerrell, SlayerNZ and contributors.
Bundled community artwork (MIT): Rob-Manders and Andechs75 - see the licence files
under plugin\com.mhwlng.elite.sdPlugin\Images\Optional\Community.
Ajazz / Stream Dock support, artwork defaults, and the profile bridge added on top.
