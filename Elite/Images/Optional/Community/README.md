# Community icon sets

Third-party artwork bundled so that every button has usable default images out of the box.
Both sets are MIT licensed; each folder keeps its original `LICENSE` file, which must stay with
the artwork if you redistribute this plugin.

## RobManders

- Source: https://github.com/Rob-Manders/elite-dangerous-stream-deck-icons
- Copyright (c) 2024 Robert Manders, MIT (see `RobManders/LICENSE`)
- Drawn specifically for this plugin (mhwlng's streamdeck-elite), so the layout already matches
  what the buttons expect: a function label along the top, dynamic output in the middle, and a
  status bar along the bottom.
- Covers the Toggle, Alarm, Limpet, Power and Firegroup buttons, plus FSD, Route and Static.
- Author's notes: artwork uses the Rubik typeface, and was verified on a Stream Deck XL.
  Matching pip/text colours are Amber `#d18105`, Dark Amber `#7f4400`, Red `#ff4646`,
  Dark Red `#4c1515`, Light Blue `#a7d7d6`.

## Andechs75

- Source: https://github.com/Andechs75/Elite-Dangerous-Streamdeck-Icons
- MIT (see `Andechs75/LICENSE`)
- Broader set organised by subject: Cam, Countermeasures, Fighter, FSS, Map, Mode, PIP, Ship,
  Target and UI. Used here for the power pips, countermeasures and fighter order artwork that
  the other set does not cover.

## Adding your own

Anything dropped under `Images\Optional` ships with the plugin automatically - the csproj
wildcards the folder - and is selectable from any button's image picker. To make a file a
*default*, add it to the table in `Elite\DefaultImages.cs`.
