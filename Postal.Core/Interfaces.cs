using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Postal.Core
{
    public interface IRequest
    {
        int Tag
        {
            get;
        }

        object InvokeReceived();
    }

    public interface IResponse
    {
    }
}