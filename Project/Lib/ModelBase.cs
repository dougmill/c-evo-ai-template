using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AI;

namespace CevoAILib
{
    unsafe class ModelList : IReadableIdIndexedArray<ModelId, Model>
    {
        private readonly EmpireData* EmpirePtr;
        private readonly ModelData.Ptr ModelsPtr;
        private readonly AEmpire TheEmpire;
        private readonly IdIndexedList<ModelId, Model> Models = new IdIndexedList<ModelId, Model>();

        public ModelList(AEmpire theEmpire)
        {
            TheEmpire = theEmpire;
            EmpirePtr = theEmpire.Data;
            ModelsPtr = EmpirePtr->ModelsData;
        }

        public int Count => EmpirePtr->NumModels;

        public Model this[ModelId id]
        {
            get
            {
                if (Models.Count < Count)
                    Update();
                return Models[id];
            }
        }

        private void Update()
        {
            var firstNewId = new ModelId((short) Models.Count);
            var lastNewId = new ModelId((short) (Count - 1));
            foreach (ModelId modelId in ModelId.Range(firstNewId, lastNewId))
                Models.Add(new Model((Empire) TheEmpire, modelId, ModelsPtr[modelId]));
        }

        public IEnumerator<Model> GetEnumerator()
        {
            if (Models.Count < Count)
                Update();
            return Models.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// basic model information as available for both own and foreign models
    /// </summary>
    abstract class ModelBase
    {
        #region abstract
        public abstract GlobalModelId GlobalId { get; }
        public abstract Nation Nation { get; }
        public abstract ModelDomain Domain { get; }
        public abstract int Attack { get; }
        public abstract int AttackPlusWithBombs { get; }
        public abstract int Defense { get; }
        public abstract int Speed { get; }
        public abstract int Cost { get; }
        public abstract int TransportCapacity { get; }
        public abstract int CarrierCapacity { get; }
        public abstract int Fuel { get; }
        public abstract int Weight { get; }
        public abstract bool HasFeature(ModelProperty feature);
        public abstract bool HasZoC { get; }
        public abstract bool IsCivil { get; }
        protected abstract ModelKind KindOnServer { get; }
        #endregion

        public ModelKind Kind => KindOnServer == ModelKind.Settlers && Speed > 150 ? ModelKind.Engineers : KindOnServer;

        /// <summary>
        /// whether model has 2 tiles observation range (distance 5) instead of adjacent locations only
        /// </summary>
        public bool HasExtendedObservationRange => KindOnServer == ModelKind.SpecialCommando ||
                                                   Domain == ModelDomain.Air ||
                                                   HasFeature(ModelProperty.RadarSonar) ||
                                                   HasFeature(ModelProperty.AcademyTraining) ||
                                                   CarrierCapacity > 0;

        // Note that the Gliders special model is marked as having the SpyPlane feature, so they're covered here too.
        public bool CanInvestigateLocations =>
            KindOnServer == ModelKind.SpecialCommando || HasFeature(ModelProperty.SpyPlane);

        /// <summary>
        /// whether units of this model are capable of doing settler jobs
        /// </summary>
        public bool CanDoJobs => KindOnServer == ModelKind.Settlers || KindOnServer == ModelKind.Slaves;

        public bool CanCaptureCity => Domain == ModelDomain.Ground && !IsCivil;

        public bool CanBombardCity => Attack + AttackPlusWithBombs > 0 &&
                                      ((Domain == ModelDomain.Sea && HasFeature(ModelProperty.Artillery)) ||
                                       Domain == ModelDomain.Air);

        /// <summary>
        /// whether units of this model pass hostile terrain without damage
        /// </summary>
        public bool IsTerrainResistant => Domain != ModelDomain.Ground || Kind == ModelKind.Engineers;

        /// <summary>
        /// By which value the size of a city grows when a unit of this model is added to it. 0 if adding to a city is not possible.
        /// </summary>
        public int AddsToCitySize
        {
            get
            {
                switch (KindOnServer)
                {
                    case ModelKind.Settlers: return 2;
                    case ModelKind.Slaves: return 1;
                    default: return 0;
                }
            }
        }
    }

    /// <summary>
    /// own model, abstract base class
    /// </summary>
    abstract unsafe class AModel : ModelBase
    {
        protected readonly Empire TheEmpire;
        public readonly ModelId Id;
        protected readonly ModelData* Data;

        protected AModel(Empire empire, ModelId id, ModelData* data)
        {
            TheEmpire = empire;
            Id = id;
            Data = data;
        }

        protected AModel(Empire empire) // for Blueprint only
        {
            TheEmpire = empire;
            Id = new ModelId(-1);
            Data = &TheEmpire.Data->ResearchingModel;
        }

        public override string ToString() => Kind == ModelKind.OwnDesign || Kind == ModelKind.ForeignDesign
            ? $"Model{GlobalId.Developer}.{GlobalId.SerialNumber}({Attack}/{Defense}/{Speed})"
            : $"{Kind}";

        #region ModelBase Members
        /// <summary>
        /// unique model ID
        /// </summary>
        public override GlobalModelId GlobalId => Data->GlobalId;
        public override Nation Nation => TheEmpire.Us;
        public override ModelDomain Domain => Data->Domain;
        public override int Attack => Data->Attack;
        public override int AttackPlusWithBombs => Data->Features[ModelProperty.Bombs] * Data->StrengthMultiplier * 2;
        public override int Defense => Data->Defense;
        public override int Speed => Data->Speed;
        public override int Cost => Data->Cost;
        public override int CarrierCapacity => Data->Features[ModelProperty.Carrier] * Data->TransportMultiplier;
        public override int Fuel => Data->Features[ModelProperty.Fuel];
        public override int Weight => Data->Weight;

        public override int TransportCapacity =>
            Domain == ModelDomain.Air ? Data->Features[ModelProperty.AirTransport] * Data->TransportMultiplier
                                      : Data->Features[ModelProperty.SeaTransport] * Data->TransportMultiplier;

        /// <summary>
        /// Whether model has a certain feature.
        /// Does not work for capacities (Weapons, Armor, Mobility, SeaTransport, Carrier, Turbines, Bombs, Fuel),
        /// always returns false for these.
        /// </summary>
        /// <param name="feature">the feature</param>
        /// <returns>true if model has feature, false if not</returns>
        public override bool HasFeature(ModelProperty feature) =>
            feature >= ModelProperty.FirstBooleanProperty && Data->Features[feature] > 0;

        public override bool HasZoC => (Data->Flags & ModelFlags.ZoC) != 0;
        public override bool IsCivil => (Data->Flags & ModelFlags.Civil) != 0;
        protected override ModelKind KindOnServer => Data->Kind;
        #endregion

        public bool RequiresDoubleSupport => (Data->Flags & ModelFlags.DoubleSupport) != 0;

        public int TurnOfIntroduction => Data->TurnOfIntroduction;
        public int NumberBuilt => Data->UnitsBuilt;
        public int NumberLost => Data->UnitsLost;

        /// <summary>
        /// persistent custom value
        /// </summary>
        public int Status
        {
            get => Data->Status;
            set => Data->Status = value;
        }
    }

    unsafe class ForeignModelList : IReadableIdIndexedArray<ForeignModelId, ForeignModel>
    {
        private readonly EmpireData* EmpirePtr;
        private readonly ForeignModelData.Ptr ModelsPtr;
        private readonly AEmpire TheEmpire;
        private readonly IdIndexedList<ForeignModelId, ForeignModel> Models =
            new IdIndexedList<ForeignModelId, ForeignModel>();

        public ForeignModelList(AEmpire theEmpire)
        {
            TheEmpire = theEmpire;
            EmpirePtr = theEmpire.Data;
            ModelsPtr = EmpirePtr->ForeignModelsData;
        }

        public int Count => EmpirePtr->NumForeignModels;

        public ForeignModel this[ForeignModelId id]
        {
            get
            {
                if (Models.Count < Count)
                    Update();
                return Models[id];
            }
        }

        private void Update()
        {
            var firstNewId = new ForeignModelId((short) Models.Count);
            var lastNewId = new ForeignModelId((short) (Count - 1));
            foreach (ForeignModelId modelId in ForeignModelId.Range(firstNewId, lastNewId))
                Models.Add(new ForeignModel((Empire) TheEmpire, modelId, ModelsPtr[modelId]));
        }

        public IEnumerator<ForeignModel> GetEnumerator()
        {
            if (Models.Count < Count)
                Update();
            return Models.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// foreign model, abstract base class
    /// </summary>
    abstract unsafe class AForeignModel : ModelBase
    {
        protected readonly Empire TheEmpire;
        public readonly ForeignModelId Id;
        protected readonly ForeignModelData* Data;

        protected AForeignModel(Empire empire, ForeignModelId id, ForeignModelData* data)
        {
            TheEmpire = empire;
            Id = id;
            Data = data;
        }

        public override string ToString() => Kind == ModelKind.OwnDesign || Kind == ModelKind.ForeignDesign
            ? $"Model{GlobalId.Developer}.{GlobalId.SerialNumber}({Attack}/{Defense}/{Speed})"
            : $"{Kind}";

        #region ModelBase Members
        /// <summary>
        /// unique model ID
        /// </summary>
        public override GlobalModelId GlobalId => Data->GlobalId;
        public override Nation Nation => new Nation(TheEmpire, Data->NationId);
        public override ModelDomain Domain => Data->Domain;
        public override int Attack => Data->Attack;
        public override int AttackPlusWithBombs => Data->AttackPlusWithBombs;
        public override int Defense => Data->Defense;
        public override int Speed => Data->Speed;
        public override int Cost => Data->Cost;
        public override int TransportCapacity => Data->TransportCapacity;
        public override int Weight => Data->Weight;
        public override int CarrierCapacity => Domain == ModelDomain.Sea ? Data->CarrierCapacityOrFuel : 0;
        public override int Fuel => Domain == ModelDomain.Air ? Data->CarrierCapacityOrFuel : 0;

        /// <summary>
        /// Whether model has a certain feature.
        /// Does not work for capacities (Weapons, Armor, Mobility, SeaTransport, Carrier, Turbines, Bombs, Fuel),
        /// always returns false for these.
        /// </summary>
        /// <param name="feature">the feature</param>
        /// <returns>true if model has feature, false if not</returns>
        public override bool HasFeature(ModelProperty feature) =>
            feature >= ModelProperty.Navigation && Data->Features[feature];

        public override bool HasZoC => Domain == ModelDomain.Ground && Kind != ModelKind.SpecialCommando;
        public override bool IsCivil => Attack + AttackPlusWithBombs == 0 || Kind == ModelKind.SpecialCommando;
        protected override ModelKind KindOnServer => Data->Kind;
        #endregion

        public int NumberDefeated => Data->DestroyedByUs;

        #region template internal stuff
        /// <summary>
        /// INTERNAL - only access from CevoAILib classes!
        /// </summary>
        public ForeignOwnModelId OwnersModelId => Data->OwnersModelId;
        #endregion    
    }

    /// <summary>
    /// Model blueprint for military research. Class of AEmpire.Blueprint.
    /// </summary>
    unsafe class Blueprint : AModel
    {
        public Blueprint(Empire empire)
            : base(empire)
        {
        }

        public int StrengthMultiplier => Data->StrengthMultiplier;
        public int TransportMultiplier => Data->TransportMultiplier;
        public int CostMultiplier => Data->CostMultiplier;
        public int MaximumWeight => Data->MaxWeight;

        /// <summary>
        /// Set domain of model. Do this before setting properties.
        /// </summary>
        /// <param name="domain">the domain</param>
        /// <returns>result of operation</returns>
        public PlayResult SetDomain__Turn(ModelDomain domain) => TheEmpire.Researching == Advance.MilitaryResearch
            ? new PlayResult(PlayError.ResearchInProgress)
            : TheEmpire.Play(Protocol.sCreateDevModel, (int)domain);

        /// <summary>
        /// Set property of model. Do this after setting the domain. Earlier calls for the same property are voided.
        /// </summary>
        /// <param name="property">the property</param>
        /// <param name="value">for capacities: count of usage, for features: 1 = use, 0 = don't use</param>
        /// <returns>result of operation</returns>
        public PlayResult SetProperty__Turn(ModelProperty property, int value) =>
            TheEmpire.Researching == Advance.MilitaryResearch
            ? new PlayResult(PlayError.ResearchInProgress)
            : TheEmpire.Play(Protocol.sSetDevModelCap + (value << 4), (int) property);
    }
}
