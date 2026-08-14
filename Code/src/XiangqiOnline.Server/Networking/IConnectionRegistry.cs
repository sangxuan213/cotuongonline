namespace XiangqiOnline.Server.Networking
{
    public interface IConnectionRegistry
    {
        bool TryGetConnection(string connectionId, out ClientConnectionHandler connection);
    }
}
