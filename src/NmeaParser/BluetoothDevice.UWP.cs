﻿//
// Copyright (c) 2014 Morten Nielsen
//
// Licensed under the Microsoft Public License (Ms-PL) (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://opensource.org/licenses/Ms-PL.html
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#if NETFX_CORE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Devices.Bluetooth.Rfcomm;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Devices.Enumeration;
using Windows.Networking.Proximity;

namespace NmeaParser
{
	/// <summary>
	/// A Bluetooth NMEA device
	/// </summary>
	public class BluetoothDevice : NmeaDevice
	{
		private Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceService m_deviceService;
        private Windows.Networking.Proximity.PeerInformation m_devicePeer;
        private StreamSocket m_socket;
        private bool m_disposeService;
        private SemaphoreSlim m_semaphoreSlim = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Gets a list of bluetooth devices that supports serial communication
        /// </summary>
        /// <returns></returns>
        public static async Task<IEnumerable<RfcommDeviceService>> GetBluetoothSerialDevicesAsync()
        {
            string serialDeviceType = RfcommDeviceService.GetDeviceSelector(RfcommServiceId.SerialPort);
            var devices = await DeviceInformation.FindAllAsync(serialDeviceType);
            List<RfcommDeviceService> services = new List<RfcommDeviceService>();
            foreach(var d in devices)
                services.Add(await RfcommDeviceService.FromIdAsync(d.Id));
            return services;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BluetoothDevice"/> class.
        /// </summary>
        /// <param name="service">The RF Comm Device service.</param>
        /// <param name="disposeService">Whether this devicee should also dispose the RfcommDeviceService provided when this device disposes.</param>
        public BluetoothDevice(RfcommDeviceService service, bool disposeService = false)
		{
			m_deviceService = service;
            m_disposeService = disposeService;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BluetoothDevice"/> class.
        /// </summary>
        /// <param name="peer">The peer information device.</param>
        public BluetoothDevice(Windows.Networking.Proximity.PeerInformation peer)
        {
            m_devicePeer = peer;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (m_disposeService && m_deviceService != null)
                m_deviceService.Dispose();
            m_deviceService = null;
            m_devicePeer = null;
            base.Dispose(disposing);
        }

        /// <summary>
        /// Creates the stream the NmeaDevice is working on top off.
        /// </summary>
        /// <returns></returns>
        protected override async Task<System.IO.Stream> OpenStreamAsync()
		{
			var socket = new Windows.Networking.Sockets.StreamSocket();
            socket.Control.KeepAlive = true;
            if (m_devicePeer != null)
            {
                await socket.ConnectAsync(m_devicePeer.HostName, "1");
            }
            else
            {
                await socket.ConnectAsync(m_deviceService.ConnectionHostName, m_deviceService.ConnectionServiceName);
            }
			m_socket = socket;
            return null; //We're going to use WinRT buffers instead and will handle read/write, so no reason to return a stream. This is mainly done to avoid locking issues reading and writing at the same time
		}

		/// <summary>
		/// Closes the stream the NmeaDevice is working on top off.
		/// </summary>
		/// <param name="stream">The stream.</param>
		/// <returns></returns>
		protected override Task CloseStreamAsync(System.IO.Stream stream)
		{
            if(m_socket == null)
                throw new InvalidOperationException("No connection to close");
			m_socket.Dispose();
			m_socket = null;
			return Task.FromResult(true);
		}


        /// <inheritdoc />
        protected override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            // Reading and writing to the Bluetooth serial connection at the same time seems very unstable in UWP,
            // so we use a semaphore to ensure we don't read and write at the same time
            await m_semaphoreSlim.WaitAsync().ConfigureAwait(false);
            try
            {
                var r = await m_socket.InputStream.ReadAsync(buffer.AsBuffer(), (uint)count, Windows.Storage.Streams.InputStreamOptions.None);
                return (int)r.Length;
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <inheritdoc />
        public override bool CanWrite => true;

        /// <inheritdoc />
        public override async Task WriteAsync(byte[] buffer, int offset, int length)
        {
            if (m_socket == null)
                throw new InvalidOperationException("Device not open");
            // Reading and writing to the Bluetooth serial connection at the same time seems very unstable in UWP,
            // so we use a semaphore to ensure we don't read and write at the same time
            await m_semaphoreSlim.WaitAsync().ConfigureAwait(false);
            try
            {
                await m_socket.OutputStream.WriteAsync(buffer.AsBuffer(offset, length)).AsTask().ConfigureAwait(false);
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }
	}
}
#endif