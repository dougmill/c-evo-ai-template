using System;
using System.Collections.Generic;
using CevoAILib;

namespace AI
{
    sealed unsafe class Model : AModel
    {
        public Model(Empire empire, ModelId id, ModelData* data)
            : base(empire, id, data)
        {
        }
    }

    sealed unsafe class ForeignModel : AForeignModel
    {
        public ForeignModel(Empire empire, ForeignModelId id, ForeignModelData* data)
            : base(empire, id, data)
        {
        }
    }
}
