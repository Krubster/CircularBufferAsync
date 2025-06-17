using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Framework.LogicController
{
    public interface IColdLogicController
    {
        bool ShouldRunTick(long elapsedSinceLastTickMs);
        int GetTargetTickDurationMs();
    }
}
