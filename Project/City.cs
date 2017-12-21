using System;
using System.Collections.Generic;
using CevoAILib;

namespace AI
{
    sealed class City : ACity
    {
        public City(Empire empire, CityId id)
            : base(empire, id)
        {
        }
    }

    sealed class ForeignCity : AForeignCity
    {
        public ForeignCity(Empire empire, ForeignCityId id)
            : base (empire, id)
        {
        }

        public CitySpyReport LatestSpyReport => IsSpiedOut && GetSpyReport(out CitySpyReport report).OK ? report
            : TheEmpire.Persistent.OldSpyReports.TryGetValue(PersistentId, out report) ? report
            : null;
    }
}
