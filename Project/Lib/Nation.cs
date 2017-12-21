using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Common;
using CevoAILib.Diplomacy;
using AI;
using System.Linq;
using System.Runtime.InteropServices;

namespace CevoAILib
{
    enum Phase { BeginOfTurn, Turn, EndOfTurn, ForeignTurn }
    enum GameOverCause { SpaceShip, Conquest }

    enum ServerVersion
    {
        // There are other rules changes not listed here, but they don't seem likely to affect opponent AI behavior in a
        // relevant way. For example, the Statue of Liberty's effect used to be on Michelangelo's Chapel, and how
        // corruption is calculated when you don't have a Palace was changed.
        Unknown = 0,
        // Longboats used to have speed 350 instead of the current 250.
        LongboatSpeedReduced = 0x00000EF0,
        // Workers used to do things immediately when ordered, which allowed such things as building a road and then
        // using it in the same turn.
        WorkMovedToEndOfTurn = 0x00000EF0,
        // It used to be that all cities, not just your own, negated zone of control, making it possible to capture a
        // city despite adjacent enemy units.
        CaptureInZocRemoved = 0x00000EF0,
        // Shinkansen used to allow moving units that had no movement left, such as after an attack.
        ShinkansenWhenOutOfMovementDisabled = 0x000100F1,
        // It used to be allowed to break multiple treaties all at once, even going from Alliance to War in one turn.
        BreakTreatyDelayAdded = 0x00010100,
        // It used to be allowed to combine the submarine and sea transport unit features.
        SubmarineTransportsRemoved = 0x00010103,
        // Artillery used to be able to attack submarines.
        SubmarinesMadeImmuneToArtillery = 0x00010200,
        Current = 0x00010200
    }

    /// <summary>
    /// Various names of known AIs.
    /// </summary>
    static class KnownAINames
    {
        public const string LocalPlayer = "Local Player"; // also used for supervisor
        public const string Tournament = "AI Tournament";
        public const string AI_UO = "AI_UO_01475.dll";
        public const string ArtificialStupidity = "AS - Artificial Stupidity in Delphi v0.07.07b";
        public const string CapitalAI = "CapitalAI B";
        public const string Crystal = "Crystal (19 July 2008)";
        public const string K_IA_I = "K IA I  for C-evo 1  (06-2006)";
        public const string Liberator = "Liberator";
        public const string Seti = "SETI V0.3";
        public const string Standard = "Standard AI";
    }

    struct Economy
    {
        public readonly int TaxRate;
        public readonly int Research;
        public readonly int Wealth;

        public Economy(int taxRate, int wealth) => (TaxRate, Wealth, Research) = (taxRate, 100 - taxRate - wealth, wealth);

        public override string ToString() => $"T{TaxRate} R{Research} W{Wealth}";
    }

    unsafe struct Nation
    {
        public static Nation None = new Nation(null, new NationId(-1));

        private readonly AEmpire TheEmpire;
        public readonly NationId Id;

        public Nation(AEmpire empire, NationId id) => (TheEmpire, Id) = (empire, id);

        public override string ToString() => Id.ToString();

        public static bool operator ==(Nation nation1, Nation nation2) => nation1.Id == nation2.Id;
        public static bool operator !=(Nation nation1, Nation nation2) => nation1.Id != nation2.Id;

        public override int GetHashCode() => Id.GetHashCode();
        public override bool Equals(object obj)
        {
            Debug.Assert(obj is Nation);
            return Id == ((Nation)obj).Id;
        }

        public EmpireReport* Report => TheEmpire.Data->EmpireReports[Id];

        /// <summary>
        /// whether this nation is still in the game
        /// </summary>
        public bool Subsists => Id.IsValid && TheEmpire.Data->AliveNations[Id];

        /// <summary>
        /// whether this nation has a specific wonder in one of its cities AND this wonder's effect has not yet expired
        /// </summary>
        /// <param name="wonder">the wonder</param>
        /// <returns>true if nation has wonder and wonder is effective, false if it has not or wonder is expired</returns>
        public bool HasWonder(Building wonder) => Id.IsValid && TheEmpire.Data->Wonders[wonder].OwnerId == Id;

        /// <summary>
        /// government form of this nation
        /// </summary>
        public Government Government => this == TheEmpire.Us ? TheEmpire.Data->Government : Report->Government;

        /// <summary>
        /// credibility of this nation
        /// </summary>
        public int Credibility => this == TheEmpire.Us ? TheEmpire.Data->Credibility : Report->Credibility;

        /// <summary>
        /// colony ship of this nation
        /// </summary>
        public ColonyShipParts ColonyShip => TheEmpire.Data->ColonyShips[Id];

        /// <summary>
        /// difficulty level/handicap of this nation
        /// </summary>
        public DifficultyLevel Difficulty => TheEmpire.DifficultyLevels[Id];

        /// <summary>
        /// The name of the AI running this nation
        /// </summary>
        public string AIName => TheEmpire.AINames[Id];

        /// <summary>
        /// The version of the game that the AI running this nation was written for. This affects some of the rules with
        /// regard to this nation.
        /// </summary>
        public ServerVersion AIWrittenForServerVersion => TheEmpire.AIServerVersions[Id];

        /// <summary>
        /// The movement speed of this nation's longboats.
        /// </summary>
        public int LongboatSpeed => AIWrittenForServerVersion < ServerVersion.LongboatSpeedReduced ? 350 : 250;

        /// <summary>
        /// Whether this nation's workers do their work during the turn rather than after.
        /// </summary>
        public bool WorkersActDuringTurn => AIWrittenForServerVersion < ServerVersion.WorkMovedToEndOfTurn;

        /// <summary>
        /// Whether this nation can ignore zone of control when capturing cities.
        /// </summary>
        public bool CanCaptureInZoC => AIWrittenForServerVersion < ServerVersion.CaptureInZocRemoved;

        /// <summary>
        /// Whether this nation can move ground units on railroad with Shinkansen when their remaining movement is 0.
        /// </summary>
        public bool CanMoveOnShinkansenWhenNoMoveLeft =>
            AIWrittenForServerVersion < ServerVersion.ShinkansenWhenOutOfMovementDisabled;

        /// <summary>
        /// Whether this nation can ignore the 3 turn delay rule on breaking treaties.
        /// </summary>
        public bool CanBreakTreatiesQuickly => AIWrittenForServerVersion < ServerVersion.BreakTreatyDelayAdded;

        /// <summary>
        /// Whether this nation can make submarine transport designs.
        /// </summary>
        public bool CanTransportOnSubmarines => AIWrittenForServerVersion < ServerVersion.SubmarineTransportsRemoved;

        /// <summary>
        /// Whether this nation can attack submarines with ground artillery units.
        /// </summary>
        public bool CanBombardSubmarines => AIWrittenForServerVersion < ServerVersion.SubmarinesMadeImmuneToArtillery;
    }

    interface IDossier
    {
        int TurnOfReport { get; }
        int Treasury { get; }
        bool Has(Advance advance);
        bool HasAlmost(Advance advance);
        int FutureTechnology(Advance advance);
        Advance Researching { get; }
        int ResearchPile { get; }
        Relation RelationTo(Nation nation);
    }

    /// <summary>
    /// own empire, abstract base class
    /// </summary>
    abstract unsafe class AEmpire : IDossier
    {
        #region abstract
        protected abstract void NewGame();
        protected abstract void Resume();
        protected abstract void OnTurn();
        protected abstract void OnStealAdvance(Advance[] selection);
        protected abstract void OnForeignMove(MovingUnit unit);
        protected abstract void OnBeforeForeignCapture(Nation nation, ICity city);
        protected abstract void OnAfterForeignCapture();
        protected abstract void OnBeforeForeignAttack(MovingUnit attacker);
        protected abstract void OnAfterForeignAttack();
        protected abstract void OnUnitChanged(Location location);
        protected abstract void OnChanceToNegotiate(
            Phase situation, Nation opponent, ref bool wantNegotiation, ref bool cancelTreatyIfRejected);
        protected abstract void OnNegotiate(Negotiation negotiation);
        protected abstract void OnVictory(GameOverCause cause);
        protected abstract void OnDefeat(GameOverCause cause);
        #endregion

        public readonly NationId Id;
        public readonly Nation Us;
        public readonly Map Map;
        public readonly UnitList Units;
        public readonly ForeignUnitList ForeignUnits;
        public readonly ModelList Models;
        public readonly ForeignModelList ForeignModels;
        public readonly CityList Cities;
        public readonly ForeignCityList ForeignCities;
        public readonly DifficultyList DifficultyLevels;
        public readonly IReadableIdIndexedArray<NationId, string> AINames;
        public readonly IReadableIdIndexedArray<NationId, ServerVersion> AIServerVersions;

        /// <summary>
        /// Model blueprint for military research.
        /// </summary>
        public readonly Blueprint Blueprint;

        /// <summary>
        /// persistent memory
        /// </summary>
        public readonly Persistent Persistent;

        protected AEmpire(int nationId, IntPtr serverPtr, IntPtr dataPtr, bool isNewGame)
        {
            NewGameData* data = (NewGameData*) dataPtr;
            CallServer = (ServerCall) Marshal.GetDelegateForFunctionPointer(serverPtr, typeof(ServerCall));
            Id = new NationId(nationId);
            Data = data->EmpireData[Id];
            DifficultyLevel = data->Difficulties[Id];
            DifficultyLevels = data->Difficulties;
            TurnWhenGameEnds = data->MaxTurn;
            IsNewGame = isNewGame;

            Us = new Nation(this, Id);
            Map = new Map(this, data->MapWidth, data->MapHeight, data->LandMassPercent);
            Map.InitLocationsCache();
            Units = new UnitList(this);
            ForeignUnits = new ForeignUnitList(this);
            Models = new ModelList(this);
            ForeignModels = new ForeignModelList(this);
            Cities = new CityList(this);
            ForeignCities = new ForeignCityList(this);
            Blueprint = new Blueprint((Empire) this);
            Persistent = new Persistent((Empire) this, Data->CustomData);

            var aiNames = new IdIndexedArray<NationId, string>(Cevo.MaxNumberOfNations);
            AINames = aiNames;
            foreach (NationId id in NationId.Range(Cevo.MaxNumberOfNations))
            {
                if (!Data->AliveNations[id]) continue;
                sbyte* name = null;
                Play(Protocol.sGetAIInfo, id, &name);
                aiNames[id] = new string(name);
            }

            // UGLY hack - there is no server command to ask what version of the game an AI was written for, or the
            // consequences on what game rules it follows. So, here's some unsanctioned poking around in server memory
            // to find it. But first, check that the version running is the one we expect.
            int version;
            Play(Protocol.sGetVersion, 0, &version);
            EmpireData* empirePtr = Data - ((IId) Id).Index;
            byte* ptr = (byte*) empirePtr;
            ptr -= 9600 * 18 + 4 + ((5 * Cevo.MaxNumberOfNations - 1) / 4 + 1) * 4; // don't ask
            int* versionsPtr = (int*) ptr;
            var aiServerVersions = new IdIndexedArray<NationId, ServerVersion>(Cevo.MaxNumberOfNations);
            AIServerVersions = aiServerVersions;
            if (version == 0x00010200 && versionsPtr[((IId) Id).Index] == 0x00010200)
                foreach (NationId id in NationId.Range(Cevo.MaxNumberOfNations))
                    aiServerVersions[id] = (ServerVersion) versionsPtr[((IId) id).Index];
        }

        // for convenience, map all members of Us to empire
        public bool Subsists => Data->AliveNations[Id];
        public Government Government => Data->Government;
        public int Credibility => Data->Credibility;
        public bool HasWonder(Building wonder) => Data->Wonders[wonder].OwnerId == Id;
        public ColonyShipParts ColonyShip => Data->ColonyShips[Id];

        #region IDossier members
        public int TurnOfReport => Turn;
        public int Treasury => Data->Money;

        /// <summary>
        /// whether an advance has been completely researched
        /// </summary>
        /// <param name="advance">the advance</param>
        /// <returns>true if researched, false if not</returns>
        public bool Has(Advance advance) => Data->Technologies[advance] >= AdvanceStatus.Researched;

        /// <summary>
        /// whether an advance has been reduced in cost, from trade or a wonder, but not yet fully researched
        /// </summary>
        /// <param name="advance">the advance</param>
        /// <returns>true if gained, false if not</returns>
        public bool HasAlmost(Advance advance) => Data->Technologies[advance] == AdvanceStatus.HalfResearched;

        /// <summary>
        /// science points collected for current research
        /// </summary>
        public int ResearchPile => Data->ResearchPile;

        /// <summary>
        /// advance currently researched
        /// </summary>
        public Advance Researching
        {
            get
            {
                Advance ad = Data->Researching;
                return ad <= Advance.None ? Advance.None : ad;
            }
        }

        /// <summary>
        /// relation to specific other nation
        /// </summary>
        /// <param name="nation">the other nation</param>
        /// <returns>the relation</returns>
        public Relation RelationTo(Nation nation) => nation == Us ? Relation.Identity : Data->Treaties[nation.Id];

        /// <summary>
        /// number of future technologies developed
        /// </summary>
        /// <param name="advance">the future technology</param>
        /// <returns>number</returns>
        public int FutureTechnology(Advance advance)
        {
            Debug.Assert(advance >= Advance.FirstFuture);
            sbyte raw = (sbyte) Data->Technologies[advance];
            return raw <= 0 ? 0 : raw;
        }
        #endregion

        public bool IsMyTurn => Phase != Phase.ForeignTurn;
        public int Turn => Data->CurrentTurn;

        /// <summary>
        /// whether a specific nation level event occurred in this turn 
        /// </summary>
        /// <param name="empireEvent">the event</param>
        /// <returns>true if the event occurred, false if not</returns>
        public bool HadEvent__Turn(EmpireEvents empireEvent) => (Data->EmpireEvents & empireEvent) != 0;

        public readonly DifficultyLevel DifficultyLevel;

        private bool IsManipulationActivated(CheatOptions manipulation) => (Data->CheatOptions & manipulation) != 0;
        public readonly int TurnWhenGameEnds;
        public int TurnOfAnarchyBegin => Data->AnarchyStartedTurn;
        public int MaximumCredibilityLeft => Data->MaxCredibility;

        /// <summary>
        /// current economy settings, call ChangeEconomy__Turn to change
        /// </summary>
        public Economy Economy => new Economy(Data->TaxRate, Data->WealthRate);

        public int Research => (from city in Cities select city.ScienceOutput).Sum();
        public int NetIncomeAvailableForMaintenance =>
            (from city in Cities select city.IncomeAvailableForMaintenance - city.Maintenance).Sum() + IncomeFromOracle;
        public int NetIncome => (from city in Cities select city.Income - city.Maintenance).Sum() + IncomeFromOracle;

        // IncomeFromOracle is always the upcoming amount for the end of this turn (or the soonest end-of-your-turn, if it's currently someone else's turn)
        public int IncomeFromOracle => Data->OracleIncome;
        public bool CanSetResearch__Turn(Advance advance) => TestPlay(Protocol.sSetResearch, (int)advance).OK;
        public RelationDetails RelationDetailsTo(Nation nation) => new RelationDetails(this, nation);
        public BattleHistory BattleHistory => Data->BattleHistory;

        /// <summary>
        /// number of nations that are still in the game
        /// </summary>
        public int NumberOfSubsistingNations => Data->AliveNations.Count;

        /// <summary>
        /// set of nations that are still in the game
        /// </summary>
        public Nation[] SubsistingNations
        {
            get
            {
                Nation[] subsistingNations = new Nation[NumberOfSubsistingNations];
                NationId.BitArray aliveArray = Data->AliveNations;
                int count = 0;
                foreach (NationId nationId in NationId.Range(Cevo.MaxNumberOfNations))
                {
                    if (aliveArray[nationId])
                    {
                        subsistingNations[count] = new Nation(this, nationId);
                        count++;
                    }
                }
                return subsistingNations;
            }
        }

        /// <summary>
        /// science points to collect before the current research is complete
        /// </summary>
        public int CurrentResearchCost
        {
            get
            {
                int researchCost;
                Play(Protocol.sGetTechCost, 0, &researchCost);
                return researchCost;
            }
        }

        /// <summary>
        /// Whether a specific building is built in one of the own cities.
        /// Applicable to wonders and state improvements.
        /// In case of wonders true is only returned if this wonder's effect has not yet expired.
        /// </summary>
        /// <param name="wonder">the wonder</param>
        /// <returns>true if built and effective, false if not</returns>
        public bool Has(Building wonder) =>
            wonder < Building.WonderRange ? HasWonder(wonder) : Data->StateImprovements.IsBuilt(wonder);

        public bool Wonder_WasBuilt(Building wonder) => !Data->Wonders[wonder].CanBeBuilt;
        public bool Wonder_WasDestroyed(Building wonder) => Data->Wonders[wonder].IsDestroyed;
        public bool Wonder_IsInCity(Building wonder, ICity city) => Data->Wonders[wonder].CityId == city.PersistentId;

        public Dossier LastDossier(Nation nation) => new Dossier(this, nation);
        public MilitaryReport LastMilitaryReport(Nation nation) => new MilitaryReport(this, nation);

        #region effective methods
        /// <summary>
        /// Start revolution.
        /// </summary>
        /// <returns>result of operation</returns>
        public PlayResult Revolution__Turn() 
        { 
            PlayResult result = Play(Protocol.sRevolution);
            if (result.OK)
                InvalidateAllCityReports();
            return result;
        }

        /// <summary>
        /// Set government form. Requires AnarchyOver event to have occurred this turn.
        /// </summary>
        /// <param name="newGovernment">new government form</param>
        /// <returns>result of operation</returns>
        public PlayResult SetGovernment__Turn(Government newGovernment) 
        {
            PlayResult result = Play(Protocol.sSetGovernment, (int) newGovernment);
            if (result.Effective)
                InvalidateAllCityReports();
            return result;
        }

        /// <summary>
        /// Change economy settings.
        /// </summary>
        /// <param name="economy">new economy settings</param>
        /// <returns>result of operation</returns>
        public PlayResult ChangeEconomy__Turn(Economy economy) 
        {
            PlayResult result = Play(Protocol.sSetRates, (economy.TaxRate / 10 & 0xf) + ((economy.Wealth / 10 & 0xf) << 4));
            if (result.Effective)
                InvalidateAllCityReports();
            return result;
        }

        /// <summary>
        /// Set new advance to research. Requires ResearchComplete event to have occurred this turn.
        /// If advance is MilitaryResearch Blueprint must have been designed as desired already.
        /// </summary>
        /// <param name="advance">advance to research</param>
        /// <returns>result of operation</returns>
        public PlayResult SetResearch__Turn(Advance advance) => Play(Protocol.sSetResearch, (int) advance);

        /// <summary>
        /// Steal advance as offered by the temple of zeus wonder.
        /// Call from OnStealAdvance handler only.
        /// </summary>
        /// <param name="advance">the advance to steal</param>
        /// <returns>result of operation</returns>
        public PlayResult StealAdvance__Turn(Advance advance) => Play(Protocol.sStealTech, (int) advance);

        #endregion

        #region template internal stuff
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ServerCall(int command, int nation, int subject, void* data);

        private readonly ServerCall CallServer;
        private readonly bool IsNewGame;

        /// <summary>
        /// INTERNAL - only access from CevoAILib classes!
        /// </summary>
        public readonly EmpireData* Data;

        private bool Called = false;
        private Phase Phase = Phase.ForeignTurn;
        private bool ForeignMoveSkipped;
        private bool ForeignMoveIsCapture;
        private bool GameOverNotified = false;

        // diplomacy
        private Nation PossibleNegotiationWith = Nation.None;
        private bool CancelTreatyIfRejected = false;
        private readonly List<Nation> NationsContacted = new List<Nation>();
        private readonly List<Nation> NationsNotToContact = new List<Nation>();
        private Negotiation CurrentNegotiation = null;

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public void StealAdvance()
        {
            List<Advance> stealable = new List<Advance>();
            for (Advance testAdvance = Advance.FirstCommon; testAdvance <= Advance.LastCommon; testAdvance++)
            {
                if (TestPlay(Protocol.sStealTech, (int)testAdvance).OK)
                    stealable.Add(testAdvance);
            }
            if (stealable.Count > 0)
                OnStealAdvance(stealable.ToArray());
        }

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult Play(int command, int subject, void* data) =>
            new PlayResult(CallServer(command, ((IId) Id).Index, subject, data));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult Play(int command, IId subject, void* data) =>
            new PlayResult(CallServer(command, ((IId) Id).Index, subject.Index, data));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult Play(int command, int subject, int data) =>
            new PlayResult(CallServer(command, ((IId) Id).Index, subject, &data));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult Play(int command, IId subject, int data) =>
            new PlayResult(CallServer(command, ((IId) Id).Index, subject.Index, &data));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult Play(int command, int subject) =>
            new PlayResult(CallServer(command, ((IId) Id).Index, subject, null));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult Play(int command, IId subject) =>
            new PlayResult(CallServer(command, ((IId) Id).Index, subject.Index, null));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult Play(int command) => new PlayResult(CallServer(command, ((IId) Id).Index, 0, null));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult TestPlay(int command, int subject, void* data) =>
            new PlayResult(CallServer(command & ~Protocol.sExecute, ((IId) Id).Index, subject, data));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult TestPlay(int command, IId subject, void* data) =>
            new PlayResult(CallServer(command & ~Protocol.sExecute, ((IId) Id).Index, subject.Index, data));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult TestPlay(int command, int subject, int data) =>
            new PlayResult(CallServer(command & ~Protocol.sExecute, ((IId) Id).Index, subject, &data));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult TestPlay(int command, IId subject, int data) =>
            new PlayResult(CallServer(command & ~Protocol.sExecute, ((IId) Id).Index, subject.Index, &data));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult TestPlay(int command, int subject) =>
            new PlayResult(CallServer(command & ~Protocol.sExecute, ((IId) Id).Index, subject, null));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult TestPlay(int command, IId subject) =>
            new PlayResult(CallServer(command & ~Protocol.sExecute, ((IId) Id).Index, subject.Index, null));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public PlayResult TestPlay(int command) =>
            new PlayResult(CallServer(command & ~Protocol.sExecute, ((IId) Id).Index, 0, null));

        /// <summary>
        /// INTERNAL - only call from CevoAILib classes!
        /// </summary>
        public void InvalidateAllCityReports() { foreach (City city in Cities) city.InvalidateReport(); }

        public delegate void StartOfTurnOrResumeHandler();
        public event StartOfTurnOrResumeHandler OnStartOfTurnOrResume;


        /// <summary>
        /// INTERNAL - only call from Plugin class!
        /// </summary>
        public void Process(int command, IntPtr dataPtr)
        {
            int* data = (int*) dataPtr;
            if (!Called)
            {
                if (IsNewGame)
                    NewGame();
                // Bug: cShowTurnChange is sent during game loading when it shouldn't be. Turn number stays zero until
                // loading is finished, so use that to detect and ignore these spurious commands.
                else if (command != Protocol.cShowTurnChange || Turn != 0)
                {
                    OnStartOfTurnOrResume?.Invoke();
                    Resume();
                }
                else
                    return;
            }
            Called = true;

            switch (command)
            {
                case Protocol.cTurn:
                case Protocol.cContinue:
                    {
                        if (!Subsists)
                        {
                            if (!GameOverNotified)
                            {
                                OnDefeat(GameOverCause.Conquest);
                                GameOverNotified = true;
                            }
                            Play(Protocol.sTurn);
                            return;
                        }

                        if (command == Protocol.cTurn)
                        {
                            Phase = Phase.BeginOfTurn;
                            NationsContacted.Clear();
                            Map.JobInfo.Clear();
                            if (Researching != Advance.MilitaryResearch)
                                Play(Protocol.sCreateDevModel, (int) ModelDomain.Ground); // keep blueprint current
                            OnStartOfTurnOrResume?.Invoke();
                            if (ColonyShip.IsComplete && !GameOverNotified)
                            {
                                OnVictory(GameOverCause.SpaceShip);
                                GameOverNotified = true;
                            }
                        }
                        if (command == Protocol.cContinue && PossibleNegotiationWith != Nation.None)
                        { // that means a negotiation attempt was made but rejected
                            if (CancelTreatyIfRejected && RelationTo(PossibleNegotiationWith) >= Relation.Peace)
                                Play(Protocol.sCancelTreaty);
                        }
                        else
                            NationsNotToContact.Clear();
                        CurrentNegotiation = null;
                        PossibleNegotiationWith = Nation.None;
                        CancelTreatyIfRejected = false;

                        InvalidateAllCityReports(); // turn begin and after negotiation

                        while (true)
                        {
                            if (Government != Government.Anarchy)
                            {
                                foreach (Nation nation in SubsistingNations)
                                {
                                    if (nation != Us &&
                                        RelationTo(nation) != Relation.NoContact &&
                                        nation.Government != Government.Anarchy &&
                                        !(NationsContacted.Contains(nation)) &&
                                        !(NationsNotToContact.Contains(nation)) &&
                                        TestPlay(Protocol.scContact + (((IId) nation.Id).Index << 4)).OK)
                                    {
                                        bool wantNegotiation = false;
                                        CancelTreatyIfRejected = false;
                                        OnChanceToNegotiate(Phase, nation, ref wantNegotiation, ref CancelTreatyIfRejected);
                                        if (wantNegotiation)
                                        {
                                            NationsContacted.Add(nation);
                                            PossibleNegotiationWith = nation;
                                            Play(Protocol.scContact + (((IId) nation.Id).Index << 4));
                                            return;
                                        }
                                        else
                                            NationsNotToContact.Add(nation);
                                    }
                                }
                            }
                            if (Phase == Phase.BeginOfTurn)
                            {
                                Phase = Phase.Turn;
                                OnTurn();
                                Phase = Phase.EndOfTurn;
                                NationsContacted.Clear();
                                NationsNotToContact.Clear();
                            }
                            else
                                break;
                        }

                        Phase = Phase.ForeignTurn;
                        Play(Protocol.sTurn);
                        break;
                    }

                case Protocol.scContact:
                    {
                        if (Phase != Phase.ForeignTurn)
                            throw new Exception("Error in logic: scContact should not be called in own turn!");
                        PossibleNegotiationWith = new Nation(this, new NationId(*data));
                        bool wantNegotiation = false;
                        bool dummy = false;
                        OnChanceToNegotiate(Phase, PossibleNegotiationWith, ref wantNegotiation, ref dummy);
                        Play(wantNegotiation ? Protocol.scDipStart : Protocol.scReject);
                        break;
                    }

                case Protocol.scDipStart:
                case Protocol.scDipNotice:
                case Protocol.scDipAccept:
                case Protocol.scDipCancelTreaty:
                case Protocol.scDipOffer:
                case Protocol.scDipBreak:
                    {
                        if (CurrentNegotiation == null)
                            CurrentNegotiation = new Negotiation(this, Phase, PossibleNegotiationWith);
                        PossibleNegotiationWith = Nation.None;
                        CancelTreatyIfRejected = false;

                        if (command == Protocol.scDipStart) // no statements yet in this negotiation
                        {
                            if (Phase == Phase.ForeignTurn)
                                throw new Exception("Error in logic: scDipStart should only be called in own turn!");
                            CurrentNegotiation.SetOurNextStatement(new SuggestEnd());
                        }
                        else
                        {
                            IStatement oppenentStatement = StatementFactory.OpponentStatementFromCommand(this, CurrentNegotiation.Opponent, command, data);
                            if (CurrentNegotiation.History.Count == 0 && Phase == Phase.ForeignTurn)
                                CurrentNegotiation.History.Insert(0, new ExchangeOfStatements(new SuggestEnd() /*imaginary, has not happened*/, oppenentStatement));
                            else
                                CurrentNegotiation.History.Insert(0, new ExchangeOfStatements(CurrentNegotiation.OurNextStatement, oppenentStatement));
                            if (oppenentStatement is CancelTreaty || oppenentStatement is Break)
                                CurrentNegotiation.SetOurNextStatement(new Notice()); // initialize with standard response
                            else
                                CurrentNegotiation.SetOurNextStatement(new SuggestEnd()); // initialize with standard response
                        }

                        OnNegotiate(CurrentNegotiation);

                        if (CurrentNegotiation.OurNextStatement is SuggestTrade trade)
                        {
                            fixed (int* tradeData = trade.Data)
                            {
                                Play(CurrentNegotiation.OurNextStatement.Command, 0, tradeData);
                            }
                        }
                        else
                            Play(CurrentNegotiation.OurNextStatement.Command);
                        break;
                    }

                case Protocol.cShowEndContact: { CurrentNegotiation = null; break; }

                case Protocol.cShowMoving:
                case Protocol.cShowCapturing:
                    {
                        if (Phase != Phase.ForeignTurn)
                            throw new Exception("Error in logic: cShowMoving should not be called in own turn!");
                        ForeignMoveIsCapture = (command == Protocol.cShowCapturing);
                        Relation relationToMovingNation = Data->Treaties[new NationId(*data)];
                        ForeignMoveSkipped = !ForeignMoveIsCapture && relationToMovingNation == Relation.Alliance; 
                            // allied movement: low relevance, high frequency, so skip
                        if (!ForeignMoveSkipped)
                        {
                            MovingUnit unit = new MovingUnit(this, (MovingUnitData*) data);
                            OnForeignMove(unit);
                            if (ForeignMoveIsCapture)
                                OnBeforeForeignCapture(unit.Nation, unit.ToLocation.City);
                        }
                        break;
                    }

                case Protocol.cShowAttacking:
                    {
                        if (Phase != Phase.ForeignTurn)
                            throw new Exception("Error in logic: cShowAttacking should not be called in own turn!");
                        MovingUnit unit = new MovingUnit(this, (MovingUnitData*) data);
                        OnBeforeForeignAttack(unit);
                        break;
                    }

                case Protocol.cShowUnitChanged:
                    {
                        if (Phase != Phase.ForeignTurn)
                            throw new Exception("Error in logic: cShowAttacking should not be called in own turn!");
                        Location location = new Location(this, *((LocationId*) data));
                        OnUnitChanged(location);
                        break;
                    }

                case Protocol.cShowAfterMove:
                    {
                        if (Phase != Phase.ForeignTurn)
                            throw new Exception("Error in logic: cShowAfterMove should not be called in own turn!");
                        if (ForeignMoveIsCapture && !ForeignMoveSkipped)
                        {
                            OnAfterForeignCapture(); // cShowCityChanged was already called here
                        }
                        break;
                    }

                case Protocol.cShowAfterAttack:
                    {
                        if (Phase != Phase.ForeignTurn)
                            throw new Exception("Error in logic: cShowAfterAttack should not be called in own turn!");
                        OnAfterForeignAttack();
                        break;
                    }

                case Protocol.cShowCityChanged:
                    {
                        if (Phase != Phase.ForeignTurn)
                            throw new Exception("Error in logic: cShowCityChanged should not be called in own turn!");
                        break;
                    }

                case Protocol.cShowTurnChange:
                    {
                        if (!GameOverNotified && !Subsists)
                        {
                            OnDefeat(GameOverCause.Conquest);
                            GameOverNotified = true;
                        }
                        else if (!GameOverNotified && NumberOfSubsistingNations == 1)
                        {
                            OnVictory(GameOverCause.Conquest);
                            GameOverNotified = true;
                        }
                        else if (!GameOverNotified && ColonyShip.IsComplete)
                        {
                            OnVictory(GameOverCause.SpaceShip);
                            GameOverNotified = true;
                        }
                        else if (!GameOverNotified)
                        {
                            // There is unfortunately no provided way whatsoever to detect when another nation has won
                            // the game by building the colony ship. The cShowShipChange command is not sent when the
                            // final part is built, and even the record of ship parts the builder has is not updated in
                            // anyone else's copy of data, except player 0 when the final "the game is over now"
                            // announcement turn comes up. cTurn will not be sent to anyone (except player 0) after the
                            // ship is built. There is only one command that will always be sent to everyone at least
                            // once after the ship is completed - cShowTurnChange for the beginning of player 0's turn.
                            // Even in responding to this command, the own empire's copy of the list of ship parts is
                            // not updated, so checking it is useless.
                            //
                            // So, I resort to the only solution available - an extremely hacky one: using the layout of
                            // server data, and one known point within it, to find and check the game's master copy of
                            // ship parts data. This layout, unlike the ones described in SharedMemory.cs, is not part
                            // of the intended interface between the server and the AI modules, so there is no guarantee
                            // it won't change in future versions.
                            //
                            // Unfortunately, I see no other way to detect this event without changing game server code
                            // to add notification of it. I couldn't even find a workaround in different responses to
                            // attempted calls back to the server. Attempting to contact yourself in diplomacy comes
                            // close (it would return RulesViolation if the game is over, PrerequisitesMissed if not),
                            // but that would only work during your turn and no one gets a turn after the ship is
                            // finished. Trying it outside of your turn gets NoTurn error instead.
                            // But first, check that the server version running is the one we expect.
                            int version;
                            Play(Protocol.sGetVersion, 0, &version);
                            if (version != 0x00010200 || AIServerVersions[Id] == ServerVersion.Unknown) break;

                            EmpireData* afterData = Data + Cevo.MaxNumberOfNations - ((IId) Id).Index;
                            int* difficulties = (int*) afterData;
                            int* afterDifficulties = difficulties + Cevo.MaxNumberOfNations;
                            ColonyShipsList* ships = (ColonyShipsList*) afterDifficulties;
                            foreach (NationId id in NationId.Range(Cevo.MaxNumberOfNations))
                            {
                                if ((*ships)[id].IsComplete && id != Id)
                                {
                                    OnDefeat(GameOverCause.SpaceShip);
                                    GameOverNotified = true;
                                }
                            }
                        }
                        break;
                    }
            }
        }
        #endregion
    }

    unsafe struct RelationDetails
    {
        private readonly AEmpire TheEmpire;
        private readonly Nation Nation;

        public RelationDetails(AEmpire empire, Nation nation)
        {
            TheEmpire = empire;
            Nation = nation;
        }

        private EmpireReport* Report => TheEmpire.Data->EmpireReports[Nation.Id];

        public int TurnOfLastNegotiation => Report->TurnOfContact;
        public int TurnOfLastCancellingTreaty => TheEmpire.Data->LastCancelTreatyTurns[Nation.Id];
        public int TurnOfPeaceEvacuationBegin => TheEmpire.Data->EvacuationStartTurns[Nation.Id];
    }

    unsafe struct Dossier : IDossier
    {
        private readonly AEmpire TheEmpire;
        private readonly Nation Nation;

        public Dossier(AEmpire empire, Nation nation)
        {
            TheEmpire = empire;
            Nation = nation;
        }

        public override string ToString() => TurnOfReport >= 0 ? $"{TurnOfReport}" : "NA";

        public bool IsAvailable => TurnOfReport >= 0;

        private EmpireReport* Report => TheEmpire.Data->EmpireReports[Nation.Id];

        public int TurnOfReport => Report->TurnOfDossier;
        public int Treasury => Report->Money;

        /// <summary>
        /// whether an advance has been completely researched
        /// </summary>
        /// <param name="advance">the advance</param>
        /// <returns>true if researched, false if not</returns>
        public bool Has(Advance advance) => Report->Technologies[advance] >= AdvanceStatus.Researched;

        /// <summary>
        /// whether an advance was gained from a trade with another nation or from the temple of zeus wonder
        /// </summary>
        /// <param name="advance">the advance</param>
        /// <returns>true if gained, false if not</returns>
        public bool HasAlmost(Advance advance) => Report->Technologies[advance] == AdvanceStatus.HalfResearched;

        /// <summary>
        /// science points collected for current research
        /// </summary>
        public int ResearchPile => Report->ResearchPile;

        /// <summary>
        /// advance currently researched
        /// </summary>
        public Advance Researching
        {
            get
            {
                Advance ad = Report->Researching;
                return ad < 0 ? Advance.None : ad;
            }
        }

        /// <summary>
        /// relation to specific other nation
        /// </summary>
        /// <param name="thirdNation">the other nation</param>
        /// <returns>the relation</returns>
        public Relation RelationTo(Nation thirdNation) =>
            thirdNation == Nation ? Relation.Identity : Report->Treaties[thirdNation.Id];

        /// <summary>
        /// number of future technologies developed
        /// </summary>
        /// <param name="advance">the future technology</param>
        /// <returns>number</returns>
        public int FutureTechnology(Advance advance)
        {
            sbyte raw = (sbyte)Report->Technologies[advance];
            return raw <= 0 ? 0 : raw;
        }
    }

    unsafe struct MilitaryReport
    {
        private readonly AEmpire TheEmpire;
        private readonly Nation Nation;

        public MilitaryReport(AEmpire empire, Nation nation)
        {
            TheEmpire = empire;
            Nation = nation;
        }

        public override string ToString() => TurnOfReport >= 0 ? $"{TurnOfReport}" : "NA";

        public bool IsAvailable => TurnOfReport >= 0;

        private EmpireReport* Report => TheEmpire.Data->EmpireReports[Nation.Id];

        public int TurnOfReport => Report->TurnOfMilitaryReport;

        public int this[ForeignModel model] => Report->UnitCounts[model.OwnersModelId];
    }
}
