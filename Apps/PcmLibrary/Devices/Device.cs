﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PcmHacking
{
    public enum TimeoutScenario
    {
        Undefined = 0,
        ReadProperty,
        SendKernel,
        ReadMemoryBlock,
    }

    public enum VpwSpeed
    {
        Standard, // 10.4 kbps
        FourX, // 41.2 kbps
    }

    /// <summary>
    /// The Interface classes are responsible for commanding a hardware
    /// interface to send and receive VPW messages.
    /// 
    /// They use the IPort interface to communicate with the hardware.
    /// TODO: Move the IPort stuff into the SerialDevice class, since J2534 devices don't need it.
    /// </summary>
    public abstract class Device : IDisposable
    {
        protected ILogger Logger { get; private set; }

        public int MaxSendSize { get; protected set; }

        public int MaxReceiveSize { get; protected set; }

        public bool Supports4X { get; protected set; }

        public int ReceivedMessageCount { get { return this.queue.Count; } }

        /// <summary>
        /// Queue of messages received from the VPW bus.
        /// </summary>
        private Queue<Message> queue = new Queue<Message>();

        /// <summary>
        /// Current speed of the VPW bus.
        /// </summary>
        private VpwSpeed speed;

        /// <summary>
        /// Constructor.
        /// </summary>
        public Device(ILogger logger)
        {
            this.Logger = logger;

            // These default values can be overwritten in derived classes.
            this.MaxSendSize = 100;
            this.MaxReceiveSize = 100;
            this.Supports4X = false;
            this.speed = VpwSpeed.Standard;
        }

        /// <summary>
        /// Finalizer (invoked during garbage collection).
        /// </summary>
        ~Device()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// Clean up anything allocated by this instane.
        /// </summary>
        public virtual void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Make the device ready to communicate with the VPW bus.
        /// </summary>
        public abstract Task<bool> Initialize();

        /// <summary>
        /// Set the timeout period to wait for responses to incoming messages.
        /// </summary>
        public abstract Task SetTimeout(TimeoutScenario scenario);

        /// <summary>
        /// Send a message.
        /// </summary>
        public abstract Task<bool> SendMessage(Message message);

        /// <summary>
        /// Removes any messages that might be waiting in the incoming-message queue.
        /// </summary>
        public void ClearMessageQueue()
        {
            this.queue.Clear();
            ClearMessageBuffer();
        }
        /// <summary>
        /// Clears Serial port buffer or J2534 api buffer
        /// </summary>
        public abstract void ClearMessageBuffer();


        /// <summary>
        /// Reads a message from the VPW bus and returns it.
        /// </summary>
        public async Task<Message> ReceiveMessage()
        {
            if (this.queue.Count == 0)
            {
                await this.Receive();
            }

            lock (this.queue)
            {
                if (this.queue.Count > 0)
                {
                    return this.queue.Dequeue();
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Set the device's VPW data rate.
        /// </summary>
        public async Task<bool> SetVpwSpeed(VpwSpeed newSpeed)
        {
            if (this.speed == newSpeed)
            {
                return true;
            }

            if (!await this.SetVpwSpeedInternal(newSpeed))
            {
                return false;
            }

            this.speed = newSpeed;
            return true;
        }

        /// <summary>
        /// Set the interface to low (false) or high (true) speed
        /// </summary>
        protected abstract Task<bool> SetVpwSpeedInternal(VpwSpeed newSpeed);

        /// <summary>
        /// Clean up anything that this instance has allocated.
        /// </summary>
        protected abstract void Dispose(bool disposing);

        /// <summary>
        /// Add a received message to the queue.
        /// </summary>
        protected void Enqueue(Message message)
        {
            lock (this.queue)
            {
                this.queue.Enqueue(message);
            }
        }

        /// <summary>
        /// List for an incoming message of the VPW bus.
        /// </summary>
        protected abstract Task Receive();

        /// <summary>
        /// Calculates the time required for the given scenario at the current VPW speed.
        /// </summary>
        protected int GetVpwTimeoutMilliseconds(TimeoutScenario scenario)
        {
            int packetSize;

            switch (scenario)
            {
                case TimeoutScenario.ReadProperty:
                    // Approximate number of bytes in a get-VIN or get-OSID response.
                    packetSize = 20;
                    break;

                case TimeoutScenario.ReadMemoryBlock:
                    // Adding 20 bytes to account for the 'read request accepted' 
                    // message that comes before the read payload.
                    packetSize = 20 + this.MaxReceiveSize;

                    // Not sure why this is necessary, but AllPro 2k reads won't work without it.
                    //packetSize = (int) (packetSize * 1.1);
                    packetSize = (int) (packetSize * 2.2);
                    break;

                case TimeoutScenario.SendKernel:
                    packetSize = this.MaxSendSize + 20;
                    break;

                default:
                    throw new NotImplementedException("Unknown timeout scenario " + scenario);
            }

            int bitsPerByte = 9; // 8N1 serial
            double bitsPerSecond = this.speed == VpwSpeed.Standard ? 10.4 : 41.6;
            double milliseconds = (packetSize * bitsPerByte) / bitsPerSecond;

            // Add 10% just in case.
            return (int)(milliseconds * 1.1);
        }

        /// <summary>
        /// Estimate timeouts. The code above seems to do a pretty good job, but this is easier to experiment with.
        /// </summary>
        protected int __GetVpwTimeoutMilliseconds(TimeoutScenario scenario)
        {
            switch (scenario)
            {
                case TimeoutScenario.ReadProperty:
                    return 100;

                case TimeoutScenario.ReadMemoryBlock:
                    return 2500;

                case TimeoutScenario.SendKernel:
                    return 1000;

                default:
                    throw new NotImplementedException("Unknown timeout scenario " + scenario);
            }
        }
    }
}
