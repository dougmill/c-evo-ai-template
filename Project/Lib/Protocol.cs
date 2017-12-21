using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CevoAILib
{
    /// <summary>
    /// INTERNAL - only use from CevoAILib classes!
    /// </summary>
    static class Protocol
    {
        public const int nPl = 15;
        public const int nJob = 15;

        public const int unFortified = 0x01;
        public const int unBombsLoaded = 0x02;
        public const int unMountainDelay = 0x04;
        public const int unConscripts = 0x08;
        public const int unWithdrawn = 0x10;
        public const int unMulti = 0x80;

        public const int mdZOC = 0x01;
        public const int mdCivil = 0x02;
        public const int mdDoubleSupport = 0x04;

        public const int ciCapital = 0x01;
        public const int ciWalled = 0x02;
        public const int ciCoastalFort = 0x04;
        public const int ciMissileBat = 0x08;
        public const int ciBunker = 0x10;
        public const int ciSpacePort = 0x20;

        public const int sExecute = 0x4000; // {call command-sExecute to request return value without execution}

        // Info Request Commands
        public const int sMessage = 0x0000; 
        public const int sSetDebugMap = 0x0010; 
        public const int sRefreshDebugMap = 0x0040;
        public const int sGetChart = 0x0100; // + type shl 4
        public const int sGetTechCost = 0x0180;
        public const int sGetAIInfo = 0x01C0;
        public const int sGetVersion = 0x01E0;
        public const int sGetTileInfo = 0x0200;
        public const int sGetCityTileInfo = 0x0210;
        public const int sGetHypoCityTileInfo = 0x0220;
        public const int sGetJobProgress = 0x0230;
        public const int sGetModels = 0x0270;
        public const int sGetUnits = 0x0280;
        public const int sGetDefender = 0x0290;
        public const int sGetBattleForecast = 0x02A0;
        public const int sGetUnitReport = 0x02B0;
        public const int sGetMoveAdvice = 0x02C0;
        public const int sGetPlaneReturn = 0x02D0;
        public const int sGetBattleForecastEx = 0x02E0;
        public const int sGetCity = 0x0300;
        public const int sGetCityReport = 0x0310;
        public const int sGetCityAreaInfo = 0x0320;
        public const int sGetEnemyCityReport = 0x0330;
        public const int sGetEnemyCityAreaInfo = 0x0340;
        public const int sGetCityTileAdvice = 0x0350;
        public const int sGetCityReportNew = 0x0360;
        public const int sGetEnemyCityReportNew = 0x0370;

        // Client Deactivation Commands
        public const int sTurn = 0x4800;

        public const int sSetGovernment = 0x5100;
        public const int sSetRates = 0x5110;
        public const int sRevolution = 0x5120;
        public const int sSetResearch = 0x5200;
        public const int sStealTech = 0x5210;
        public const int sSetAttitude = 0x5300; // + concerned player shl 4
        public const int sCancelTreaty = 0x5400;

        // Model Related Commands
        public const int sCreateDevModel = 0x5800;
        public const int sSetDevModelCap = 0x5C00; // {+value shl 4}

        // Unit Related Commands
        public const int sRemoveUnit = 0x6000;
        public const int sSetUnitHome = 0x6010;
        public const int sSetSpyMission = 0x6100; // + mission shl 4
        public const int sLoadUnit = 0x6200;
        public const int sUnloadUnit = 0x6210;
        public const int sSelectTransport = 0x6220;
        public const int sMoveUnit = 0x6400; // {+dx and 7 shl 4 +dy and 7 shl 7}

        // Settlers Related Commands
        public const int sctSettlers = 0x2800;
        public const int sAddToCity = 0x6810;
        public const int sStartJob = 0x6C00; // {+job shl 4}

        // City Related Commands
        public const int sSetCityProject = 0x7001;
        public const int sBuyCityProject = 0x7010;
        public const int sSellCityProject = 0x7020;
        public const int sSellCityImprovement = 0x7101;
        public const int sRebuildCityImprovement = 0x7111;
        public const int sSetCityTiles = 0x7201;

        public const int cInitModule = 0x0000;
        public const int cReleaseModule = 0x0100;
        public const int cNewGame = 0x0800;
        public const int cLoadGame = 0x0810;
        public const int cGetReady = 0x08F0;
        public const int cBreakGame = 0x0900;
        public const int cTurn = 0x2000;
        public const int cResume = 0x2010;
        public const int cContinue = 0x2080;
        public const int cShowUnitChanged = 0x3030;
        public const int cShowAfterMove = 0x3040;
        public const int cShowAfterAttack = 0x3050;
        public const int cShowCityChanged = 0x3090;
        public const int cShowMoving = 0x3140;
        public const int cShowCapturing = 0x3150;
        public const int cShowAttacking = 0x3240;
        public const int cShowShipChange = 0x3400;
        public const int cShowTurnChange = 0x3700;
        public const int cShowEndContact = 0x3810;

        public const int scContact = 0x4900;
        public const int scReject = 0x4A00;
        public const int scDipStart = 0x4B00;
        public const int scDipNotice = 0x4B10;
        public const int scDipAccept = 0x4B20;
        public const int scDipCancelTreaty = 0x4B30;
        public const int scDipOffer = 0x4B4E;
        public const int scDipBreak = 0x4BF0;

        public const int opChoose = 0x00000000;
        public const int opCivilReport = 0x11000000; // + turn + concerned nation shl 16
        public const int opMilReport = 0x12000000; // + turn + concerned nation shl 16
        public const int opMap = 0x1F000000;
        public const int opTreaty = 0x20000000; // + suggested nation treaty
        public const int opShipParts = 0x30000000; // + number + part type shl 16
        public const int opMoney = 0x40000000; // + value
        public const int opTech = 0x50000000; // + advance
        public const int opAllTech = 0x51000000;
        public const int opModel = 0x58000000; // + model index
        public const int opAllModel = 0x59000000;
        public const int opMask = 0x7F000000;

        public const int rExecuted = 0x40000000;
        public const int rEffective = 0x20000000;
        public const int rUnitRemoved = 0x10000000;
        public const int rEnemySpotted = 0x08000000;

        public const int eEnemyDestroyed = 0x05;

        public const int mcFirstNonCap = 9;

        public const int cpIndex = 0x1FF;
        public const int cpConscripts = 0x200;
        public const int cpDisbandCity = 0x400;
        public const int cpImp = 0x800;

        public const int phStealTech = 0x02;

        // Client extension commands.
        //
        // All command numbers from 0x8000 to 0xFFFF are reserved for custom AI use. These commands have no particular
        // meaning for the server. The only things the server does with them are:
        //     1. Immediately echo the command back to the client that sent it.
        //     2. Store the command in the save game, along with its place in the turn and command sequence.
        //     3. When loading a game, send all the stored cClientEx+ commands to the client in order.
        //
        // The client is free to assign arbitrary meaning to such commands without concern for compatibility with
        // anything other than itself. This can be used to store arbitrary amounts of data in the save game, not limited
        // by the size of Status fields or the 4 kilobyte limit of the Persistent.MyData struct. Things to consider when
        // doing so:
        //     1. The subject parameter of the server call is ignored, neither echoed nor stored.
        //     2. The last 4 bits of the command (those matched by 0x000F) specify the size in 4-byte ints of the data
        //        pointed to by the data parameter. Restrict your custom meanings to the other command bits. Only the
        //        amount of data specified by these 4 bits will be stored in the save game, for a maximum of 60 bytes
        //        of data per command. If you want to store more data, you will have to split it into 60 byte chunks.
        //     3. You will have to add code to recognize your custom commands in AEmpire.Process in the Nation.cs file.
        //     4. Only what you include in the server call is stored for reload. For proper reload handling your handler
        //        of the echoed commands MUST (re)construct the entire data structure all by itself.
        //     5. The handler of the echoed/reloaded commands MUST NOT make any server calls or access server data. This
        //        includes reading from the Units and Cities lists, for example. If it does, reloading will not work
        //        correctly.
        //     6. Each command you send increases the size of the save game file. Try to avoid unnecessary calls to
        //        avoid excessively bloating savefile size. For example, save changes to a list by saving the changes,
        //        not the entire list each time.
        public const int cClientEx = 0x8000;
        public const int cDataSizeMask = 0x000F;

        // Commands for persistent data structures that are provided with the template. All of these commands include
        // one data int to identify the class of the data structure and a second to identify the specific instance. For
        // commands with no additional data, these 2 ints are the only data the command has. For commands that include
        // serialized objects, these 2 ints come immediately after the end of the final object, positioned wherever in
        // the data block the use of cExStoreDataChunk ends up stopping the final object. For some commands an index is
        // also required. This is put after the class and instance identifiers.

        // General collection commands with no additional data.
        public const int cExCollectionCreate = 0x8002;
        public const int cExCollectionClear = 0x8012;
        public const int cExCollectionDelete = 0x8022;

        // Dictionary commands with arbitrary additional data.
        public const int cExDictRemoveItem = 0x8030;
        public const int cExDictRemoveKey = 0x8040;
        public const int cExDictAddItem = 0x8050;

        // List commands with arbitrary additional data.
        public const int cExListAdd = 0x8100;
        public const int cExListRemove = 0x8110;
        public const int cExListInsert = 0x8120;
        public const int cExListSet = 0x8130;

        // List command with no additional data.
        public const int cExListRemoveAt = 0x8143;

        // Set commands with arbitrary additional data.
        public const int cExSetAdd = 0x8200;
        public const int cExSetRemove = 0x8210;

        // Designed to be sent many times in succession, each time with 60 more bytes of data for the same object(s).
        // The first time this is sent in a batch, the first int is the size of the first object (in quantity of ints),
        // and the immediately following ones are the serialized form of that object. After the last int in that object
        // comes the size of the next, then that next object's data, and so on for however many objects there are. If
        // the amount of data overflows the 60 byte limit, 60 bytes get sent in one command and a new one is started,
        // picking up right where the last command left off, even mid-object. These commands must be sent in
        // uninterrupted sequence.
        //
        // The final chunk - the only one that might be less than the full 15 ints - must use a different command,
        // which will specify what to do with the various objects and may include additional data for that purpose. If
        // the additional data would push it over one more chunk boundary, it should all be delayed to the next command,
        // with the now next-to-final chunk filled in at the end with zeros.
        public const int cExStoreDataChunk = 0xFFFF;
    }

    /// <summary>
    /// INTERNAL - only use from CevoAILib classes!
    /// </summary>
    static class ROReadPoint
    {
        public const int TestFlags = 25;
        public const int DevModel = 44;
        public const int Tech = 61;
        public const int Attitude = 85;
        public const int Wonder = 160;
        public const int Ship = 216;
        public const int NatBuilt = 261;
        public const int nBattleHistory = 272;
        public const int OracleIncome = 290;

        public const int SizeOfUn = 8;
        public const int SizeOfUnitInfo = 4;
        public const int SizeOfCity = 28;
        public const int SizeOfCityInfo = 5;
        public const int SizeOfModel = 17;
        public const int SizeOfModelInfo = 7;
    }

    /// <summary>
    /// Difficulty rating/handicap for one player. Size 4 bytes.
    /// </summary>
    enum DifficultyLevel : int { NotInGame = -1, Supervisor = 0, Easy = 1, Normal = 2, Hard = 3 }

    /// <summary>
    /// Layout in game server communication of the list of difficulty levels. 60 bytes
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 60)]
    unsafe struct DifficultyList
    {
        // really DifficultyLevel type, but C# won't allow that with fixed
        private fixed int Difficulties[Cevo.MaxNumberOfNations];

        public DifficultyLevel this[NationId nation]
        {
            get
            {
                Debug.Assert(nation.IsValid);
                fixed (int* i = Difficulties)
                {
                    return (DifficultyLevel) i[((IId) nation).Index];
                }
            }
        }
    }

    /// <summary>
    /// Layout in game server communication of the data sent on initializing for a new game.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 452)]
    unsafe struct NewGameData
    {
        public readonly int MapWidth; // 4 bytes, 4 total
        public readonly int MapHeight; // 4 bytes, 8 total
        public readonly int LandMassPercent; // 4 bytes, 12 total
        public readonly int MaxTurn; // 4 bytes, 16 total
        public readonly DifficultyList Difficulties; // 60 bytes, 76 total
        /// <summary>
        /// All entries in this array that use other AIs are null. You could cheat by accessing data of other empires,
        /// but only of other empires that use the same AI.
        /// </summary>
        public readonly EmpireData.PtrArray EmpireData; // 60 bytes, 136 total
        private fixed sbyte AssemblyPathChars[256]; // 256 bytes, 392 total
        // This array is populated only when the player is the supervisor, which will never be true for an AI. It will
        // always be filled with null.
        private readonly EmpireData.PtrArray SupervisorEmpireData; // 60 bytes, 452 total

        public string AssemblyPath
        {
            get
            {
                fixed (sbyte* chars = AssemblyPathChars)
                {
                    return new string(chars);
                }
            }
        }
    }

    /// <summary>
    /// Layout in game server communication of a set of the 3 basic resources (food, material, trade). Size 12 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 12)]
    struct BaseResourceSet
    {
        public int Food; // 4 bytes
        public int Material; // 4 bytes, 8 total
        public int Trade; // 4 bytes, 12 total

        public BaseResourceSet(int food, int material, int trade)
        {
            Food = food;
            Material = material;
            Trade = trade;
        }

        public override string ToString() => $"F{Food} M{Material} T{Trade}";
    }

    /// <summary>
    /// Layout in game server communication of information about a location. Size 16 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
    struct TileInfo
    {
        public readonly BaseResourceSet Resources; // 12 bytes, 12 total
        public readonly CityId ExploitingCity; // 2 bytes, 14 total
        private readonly short padding; // 2 bytes, 16 total

        public TileInfo(CityId hypotheticalCity)
        {
            Resources = new BaseResourceSet();
            ExploitingCity = hypotheticalCity;
            padding = 0;
        }
    }

    /// <summary>
    /// Layout in game server communication of a detailed report on a city. Size 88 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 88)]
    struct CityReportData
    {
        /// <summary>
        /// Bit array with same format as CityBasicData.ExploitedTiles. Input to city report call; data returned will
        /// be as if these tiles were the ones currently worked. Set to -1 to use the actual currently worked tiles.
        /// </summary>
        public int HypotheticalExploitedTiles; // 4 bytes
        /// <summary>
        /// Percentage tax rate. Input to city report call; data returned will be as if the current tax rate were this.
        /// Set to -1 to use the actual current tax rate.
        /// </summary>
        public int HypotheticalTaxRate; // 4 bytes, 8 total
        /// <summary>
        /// Percentage wealth/luxury rate. Input to city report call; data returned will be as if the current wealth
        /// rate were this. Set to -1 to use the actual current wealth rate.
        /// </summary>
        public int HypotheticalWealthRate; // 4 bytes, 12 total
        public readonly int Morale; // 4 bytes, 16 total
        public readonly int FoodSupport; // 4 bytes, 20 total
        public readonly int MaterialSupport; // 4 bytes, 24 total
        public readonly int CurrentProjectCost; // 4 bytes, 28 total
        // FoodStorageLimit is not actually a per-city thing, it's decided by difficulty only, but it's here anyway
        public readonly int FoodStorageLimit; // 4 bytes, 32 total
        public readonly int NumUnitsCausingUnrest; // 4 bytes, 36 total
        public readonly int Control; // 4 bytes, 40 total
        public readonly BaseResourceSet TotalResourcesFromArea; // 12 bytes, 52 total
        public readonly int NumberOfExploitedLocations; // 4 bytes, 56 total
        public readonly int FoodSurplus; // 4 bytes, 60 total
        public readonly int MaterialSurplus; // 4 bytes, 64 total
        public readonly int PollutionPlus; // 4 bytes, 68 total
        public readonly int Corruption; // 4 bytes, 72 total
        public readonly int TaxOutput; // 4 bytes, 76 total
        public readonly int ScienceOutput; // 4 bytes, 80 total
        public readonly int Wealth; // 4 bytes, 84 total
        public readonly int HappinessBalance; // 4 bytes, 88 total
    }

    /// <summary>
    /// Layout in game server communication of a full spy report on a city, which includes as much information as you
    /// have on your own cities.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 116)]
    struct CitySpyData
    {
        public readonly NationId Owner; // 1 byte, 1 total
        private readonly byte padding1; // 1 byte, 2 total
        private readonly short padding2; // 2 bytes, 4 total
        public readonly CityData Data; // 112 bytes, 116 total

        public CitySpyData(NationId owner, CityData data) => (Owner, padding1, padding2, Data) = (owner, 0, 0, data);
    }

    /// <summary>
    /// Layout in game server communication of a displacement between two locations. Size 8 bytes.
    /// Also usable for the same purpose in general AI code.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 8)]
    struct RC // relative coordinates
    {
        public static readonly RC[] NeighborRelativeCoordinates =
        {
                            new RC( 0, -2),
                    new RC(-1, -1), new RC( 1, -1),
            new RC(-2,  0),                 new RC( 2,  0),
                    new RC(-1,  1), new RC( 1,  1),
                            new RC( 0,  2),
        };

        public static readonly RC[] Distance5AreaRelativeCoordinates =
        {
                            new RC(-1, -3), new RC( 1, -3),
                    new RC(-2, -2), new RC( 0, -2), new RC( 2, -2),
            new RC(-3, -1), new RC(-1, -1), new RC( 1, -1), new RC( 3, -1),
                    new RC(-2,  0), new RC( 0,  0), new RC( 2,  0),
            new RC(-3,  1), new RC(-1,  1), new RC( 1,  1), new RC( 3,  1),
                    new RC(-2,  2), new RC( 0,  2), new RC( 2,  2),
                            new RC(-1,  3), new RC( 1,  3)
        };

        /// <summary>
        /// The relative positions of nearby "tiles" of the repeating special resource pattern. For purposes of
        /// calculations here, these tiles are rectangular, size 20 east-west by 16 north-south, and location 0 is the
        /// northwest corner of one. Incidentally, location 0 is a special resource location that may hold manganese or
        /// a land-based pre-science resource, and if it weren't on the north pole would be in the center of a visually
        /// obvious 3x3 slanted square grid layout of 9 special resource locations.
        /// 
        /// The values included are all those required to guarantee that the nearest occurrence of any given position
        /// within such a tile will be covered. For example, to determine the nearest potential manganese location
        /// (which can only occur once within a tile, on the northwest corner), it is sufficient to check the tiles
        /// with these displacements from the one you are in.
        /// </summary>
        public static readonly RC[] TileDisplacements = {
            new RC(0, 0), // same
            new RC(20, 0), // east
            new RC(8, 16), // se
            new RC(-12, 16), // sw
            new RC(-20, 0), // west
            new RC(-8, -16), // nw
            new RC(12, -16), // ne
            new RC(28, 16), // see
            new RC(16, 32), // sese
            new RC(-4, 32) // sesw
        };

        /// <summary>
        /// The positions within a tile (see TileDisplacements) where a city located there would have 4 special
        /// resources in range to work.
        /// </summary>
        public static readonly RC[] PositionsOf4ResourceSpotsInTile =
        {
            new RC(3, 1),
            new RC(5, 15),
            new RC(9, 13),
            new RC(19, 3)
        };

        /// <summary>
        /// The positions closest to a tile's northwest corner (see TileDisplacements), in the same tile or not, where
        /// a city located there would have 4 special resources in range to work.
        /// </summary>
        public static readonly RC[] Nearest4ResourceSpotsToOrigin =
        {
            new RC(3, 1),
            new RC(-1, 3),
            new RC(-3, -1),
            new RC(1, -3)
        };

        /// <summary>
        /// The positions within a tile (see TileDisplacements) where a non-plains/grassland land square would have a
        /// special resource visible from the beginning of the game. Note that on coast squares only four of these will
        /// have Fish. The (0, 0) position will have Manganese if it is a coast square.
        /// </summary>
        public static readonly RC[] PositionsOfBasicResourcesInTile =
        {
            new RC(0, 0),
            new RC(6, 2),
            new RC(18, 6),
            new RC(10, 10),
            new RC(2, 14),
        };

        /// <summary>
        /// The positions within a tile (see TileDisplacements) where a non-plains/grassland land square would have a
        /// special resource that requires Science to see and use. Note that on coast squares these will be ordinary
        /// coast with no special resource.
        /// </summary>
        public static readonly RC[] PositionsOfScienceResourcesInTile =
        {
            new RC(16, 2),
            new RC(2, 4),
            new RC(6, 12),
            new RC(12, 14),
        };

        /// <summary>
        /// The east-west portion of displacement. One step due east has a value of 2 here.
        /// </summary>
        public readonly int x;
        /// <summary>
        /// The north-south portion of displacement. One step due south has a value of 2 here.
        /// </summary>
        public readonly int y;

        public RC(int x, int y)
        {
            Debug.Assert((x & 1) == (y & 1));
            this.x = x;
            this.y = y;
        }

        public override string ToString() => $"({x},{y})";

        public static bool operator ==(RC RC1, RC RC2) => RC1.x == RC2.x && RC1.y == RC2.y;
        public static bool operator !=(RC RC1, RC RC2) => RC1.x != RC2.x || RC1.y != RC2.y;

        public override int GetHashCode() => x + (y << 16);
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is RC);
            return x == ((RC) obj).x && y == ((RC) obj).y;
        }

        public static RC operator +(RC RC1, RC RC2) => new RC(RC1.x + RC2.x, RC1.y + RC2.y);
        public static RC operator -(RC RC1, RC RC2) => new RC(RC1.x - RC2.x, RC1.y - RC2.y);

        /// <summary>
        /// Absolute distance regardless of direction.
        /// One tile counts 2 if straight (crossing an edge), 3 if diagonal (crossing a corner).
        /// </summary>
        public int Distance
        {
            get
            {
                int adx = Math.Abs(x);
                int ady = Math.Abs(y);
                return adx + ady + (Math.Abs(adx - ady) >> 1);
            }
        }
    }

    /// <summary>
    /// Layout in game server communication of information about a moving unit. Size 52 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 52)]
    struct MovingUnitData
    {
        public readonly NationId NationId; // 1 byte
        private readonly byte padding1; // 1 byte, 2 total
        private readonly short padding2; // 2 bytes, 4 total
        public readonly int HealthBefore; // 4 bytes, 8 total
        public readonly ForeignOwnModelId OwnerModelId; // 2 bytes, 10 total
        private readonly short padding3; // 2 bytes, 12 total
        public readonly ForeignModelId ModelId; // 2 bytes, 14 total
        private readonly short padding4; // 2 bytes, 16 total
        public readonly UnitFlags Flags; // 2 bytes, 18 total
        private readonly short padding5; // 2 bytes, 20 total
        public readonly LocationId FromLocation; // 4 bytes, 24 total
        public readonly RC Movement; // 8 bytes, 32 total
        public readonly int HealthAfter; // 4 bytes, 36 total
        /// <summary>
        /// The health the defending unit will have after this move, if the move is an attack.
        /// </summary>
        public readonly int DefenderHealthAfter; // 4 bytes, 40 total
        public readonly int Fuel; // 4 bytes, 44 total
        public readonly int Experience; // 4 bytes, 48 total
        public readonly int Load; // 4 bytes, 52 total
    }

    /// <summary>
    /// Layout in game server communication of information about a worker job's progress at a location.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 12)]
    struct JobProgress
    {
        public readonly int Required; // 4 bytes, 4 total
        public readonly int Done; // 4 bytes, 8 total
        public readonly int NextTurnPlus; // 4 bytes, 12 total
    }
}
