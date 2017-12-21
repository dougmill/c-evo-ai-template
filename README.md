# Updated C-evo AI Template

This is an improved and expanded version of Steffen's AI Template for [C-evo](http://c-evo.org/)

## Changes list:

### Special attention:
- The coordinate system of RC is changed from a/b to x/y. This may or may not matter to you, but if you ever make a new RC from coordinate numbers or check an RC's coordinates, you will need to update those.
- Location.Neighbors and Location.Distance5Area now return arrays of Location, not OtherLocation. If you need the old behavior, use Location.NeighborsAndOffsets and Location.Distance5AreaAndOffsets.
- Note that compiling CevoDotNet is necessary to use the template, at least until Steffen updates the official copy.
- My use of the new C# tuples language feature requires, at least for now, that System.ValueTuple.dll be available in the C-evo folder. Visual Studio should take care of this for you during development, but you'll have to bundle it with your AI dll for other people to use it.

### Versioning:
- Updated to Visual Studio 2017.
- Used language features of C# 6 and 7.
- Updated the CevoDotNet launcher to .NET 4.0, which is necessary for it to run an AI that uses .NET later than 3.5.

### Style:
- Replaced all tabs with spaces.
- Used expression bodied methods, properties, and constructors.
- Used string interpolation.
- Used pattern matching.
- Used tuples in a few places.
- Lines wrap at length 120 in most places.
- Used standard recommended C# naming patterns, in most cases.

### Server shared data structures:
- Most data made available by the server is now accessed through clear defined structs, not through inscrutable pointer arithmetic with unexplained constants.
- Some enum values or other constants have been adjusted to match the numbers the server uses. This should make no difference to custom AI code, provided you have used only the names of these values.
- Several enums and structs define data that was not previously handled by the template.

### Type safety:
- All id numbers are now strongly typed. An id of a unit is of type `UnitId`, not of type `int`. This allows the compiler to verify that you are not, for example, accidentally using a city's id number instead.
- There is a strong distinction between temporary ids, which may refer to different objects next turn, and permanent ids. Temporary ids have some extra support related to the fact that they are direct index numbers into in game data structures.

### Data consistency:
- Removed most attempts to manually trigger updating of various lists (units, cities, etc.) at each point where they could change.
- Replaced ToughSet with some purpose-built lists that update themselves automatically, based on when they detect an update is needed, not relying on other code to inform them of it.

### Bug Fixes:
- Fixed `Sprawl` bugs reported on forum at http://c-evo.org/bb/viewtopic.php?f=5&t=71 and http://c-evo.org/bb/viewtopic.php?f=5&t=74
- Fixed bug, reported on forums at http://c-evo.org/bb/viewtopic.php?f=5&t=76, that `Empire.Resume()` is called too early in reloading.
- Added check for location validity in `-` and `+` operators, as reported at http://c-evo.org/bb/viewtopic.php?f=5&t=77.

### New features:
- CevoPedia
  - Added JobInfo for Location, taking rivers into account for road/railroad build time.
  - Added what special resources a terrain can have to base terrain info.
- Cities
  - Added calculation of maintenance costs.
  - Added food storage limit.
  - Faster/cached number of exploited locations.
  - Added total income calculation, including taxes, food converted to money, overflow material, and trade goods.
  - Added income available for maintenance calculation, which does not include excess for completing a building.
  - Faster/cached list of exploited locations.
  - Added method to sell progress on current construction project.
- Foreign cities:
  - Added spy reports on enemy cities.
- Foreign nations:
  - Stored difficulty levels of all nations.
  - Added property for AI name, and several named constants for AIs I have.
  - Added property for what server version the nation's AI was written for. Used a new enum for this, with values for the versions at which various notable rules changes happened.
  - Added property for the nation's difficulty level.
  - Added properties for each of the rules changes that there are enum values for.
- Map:
  - Precalculated and cached lists of neighbors and distance 5 areas for all locations for improved performance.
  - Switched RC coordinates from a/b to x/y, for simpler math and to match the server.
  - Cached the work required and work done parts of job information. If you don't need the server to tell you how much progress you're going to make this turn, switch to the new methods.
  - Locations calculate their position in the repeating special resource pattern.
  - Defined the order of locations in the neighbors and distance 5 arrays.
  - Changed the neighbor/distance5 accessors on Location to return arrays of Location. If you need the old OtherLocation arrays, use the additional "AndOffsets" properties.
  - Added convenience properties on Location for what special resource type (basic or science) it would have, one for if ground and another for shore.
  - Added convenience property on Location for which of grassland or plains it would be, if transformed into one of those terrains.
  - Added Location methods to get how much resources the location would produce, accounting for all terrain improvements, government, etc. If you specify a city, it will also account for that city's buildings.
  - Used dictionary lookups instead of linear searches for finding city, foreign city, and foreign defender for Location.
  - Added version of GetExploitingCity__Turn() that also returns the resources produced.
- Models:
  - Removed Stage. The properties of it that might be relevant are now directly in Blueprint.
  - Added CanInvestigateLocations property.
  - Fixed bug that Engineers would say they don't add anything to city size.
- Units:
  - Added model, from location and to location, movement (as an RC), and health information to MovingUnit.
  - Added spy reports on unit stacks. Note that this comes with a behavior change - previously, if you iterated the foreign units list you would only iterate one unit (the strongest defender) per location. Now you will iterate through all known units, including every member of any spied out stacks. A new subcollection is provided for the old behavior, along with another one specifically for stacks. The collection of stacks included non-spied ones, but will only have information about one unit for those.
  - Replaced UnitByLocation with two new methods, GetForeignDefender and GerForeignStack.
- Empire:
  - Combined parameters of OnForeignMove and OnBeforeForeignAttack into one, with some additional information as well.
  - Added OnUnitChanged method, which is called when units move into and out of visibility. And in a few other circumstances that the server doesn't distinguish for this, unfortunately.
  - Added OnVictory and OnDefeat methods, which may be useful for automated statistics gathering or genetic algorithms.
  - Added method to check if cheat manipulation options are turned on (not that I expect anyone to use it).
  - Added property for current total research per turn.
  - Added properties for current net income, both total and what will be available to pay maintenance.
  - Implemented battle history, the supported-by-the-server list of all battles you have ever participated in for this game.
  - Removed Attitude, as it's completely pointless - not even displayed in game.
  - Implemented military report, the list of how many of each model a nation that you have a report on has.
- Persistence:
  - Added abstract partial implementations of IDictionary, IList, and ISet that can be used, with just a little extra to implement in a subclass, to store unlimited arbitrary amounts of data in the save game file. Be aware that the storage is done by recording the sequence of changes made to the collection, so try to be minimal about how many changes you save with them to avoid excessively increasing save file size.
  - Sample use of a persistent dictionary to store old city spy reports so that, even if you don't currently have a spy in range, you can still check what you saw the last time you did.
