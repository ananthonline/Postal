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
        int Tag
        {
            get;
        }
    }
}