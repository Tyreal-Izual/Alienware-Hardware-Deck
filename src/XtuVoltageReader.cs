using System;
using System.ServiceModel;

namespace HardwareDeck
{
    // The installed AWCC IntelCpuInfo voltage getter calls this same local XTU operation.
    // Deliberately expose only the getter: the full SDK also initializes tuning/profile state.
    [ServiceContract(Name = "IXtuTuning", Namespace = "http://tempuri.org/")]
    public interface IXtuVoltageReadOnly
    {
        [OperationContract(Action = "http://tempuri.org/IXtuTuning/GetCoreVoltageValue", ReplyAction = "http://tempuri.org/IXtuTuning/GetCoreVoltageValueResponse")]
        decimal GetCoreVoltageValue();
    }

    public sealed class XtuVoltageReader : IDisposable
    {
        public const string Source = "Intel XTU · AWCC CPU voltage source";
        readonly Func<IXtuVoltageReadOnly> open;
        readonly Func<DateTime> utcNow;
        readonly bool administrator;
        ChannelFactory<IXtuVoltageReadOnly> factory;
        IXtuVoltageReadOnly channel;
        DateTime nextAttempt = DateTime.MinValue;
        bool disposed;
        public string Status { get; private set; }

        public XtuVoltageReader()
        {
            open = Open;
            utcNow = () => DateTime.UtcNow;
            administrator = Collector.IsAdmin;
            Status = "Waiting for the AWCC/Intel XTU voltage source";
        }

        internal XtuVoltageReader(Func<IXtuVoltageReadOnly> open, Func<DateTime> utcNow, bool administrator)
        {
            this.open = open;
            this.utcNow = utcNow;
            this.administrator = administrator;
            Status = "Waiting for the AWCC/Intel XTU voltage source";
        }

        IXtuVoltageReadOnly Open()
        {
            var timeout = TimeSpan.FromMilliseconds(750);
            var binding = new NetNamedPipeBinding(NetNamedPipeSecurityMode.Transport)
            {
                OpenTimeout = timeout,
                SendTimeout = timeout,
                ReceiveTimeout = timeout,
                CloseTimeout = timeout,
                MaxReceivedMessageSize = 65536
            };
            factory = new ChannelFactory<IXtuVoltageReadOnly>(binding, new EndpointAddress("net.pipe://localhost/ExtremeTuningUtility/Tuning"));
            var result = factory.CreateChannel();
            ((IClientChannel)result).OperationTimeout = timeout;
            return result;
        }

        public double? Read()
        {
            if (disposed)
                return null;
            if (!administrator)
            {
                Status = "AWCC/Intel XTU voltage requires administrator access";
                return null;
            }

            if (channel == null && utcNow() < nextAttempt)
                return null;
            try
            {
                if (channel == null)
                    channel = open();
                var value = Values.Range((double)channel.GetCoreVoltageValue(), 0.1, 2.5);
                if (value.HasValue)
                {
                    Status = Source;
                    return value;
                }

                Status = "AWCC/Intel XTU returned no valid CPU voltage; retrying in 60 s";
            }
            catch (Exception ex)
            {
                Status = "AWCC/Intel XTU voltage unavailable (" + ex.GetType().Name + "); retrying in 60 s";
            }

            Release();
            nextAttempt = utcNow().AddSeconds(60);
            return null;
        }

        void Release()
        {
            var connection = channel as ICommunicationObject;
            if (connection != null)
                connection.Abort();
            channel = null;
            if (factory != null)
                factory.Abort();
            factory = null;
        }

        public void Dispose()
        {
            disposed = true;
            Release();
        }
    }
}
