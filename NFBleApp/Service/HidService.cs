using nanoFramework.Device.Bluetooth.GenericAttributeProfile;
using nanoFramework.Device.Bluetooth;
using System;
using System.Collections;

namespace NFBleApp.Service
{
    public class HidService
    {
        private readonly GattLocalService _service;
        // GattLocalCharacteristic _hidInformationCharacteristic;
        GattLocalCharacteristic _inputReportCharacteristic;
        private readonly ArrayList _sensors;

        public enum SensorType
        {
            Temperature,
            Humidity,
            Pressure,
            Rainfall
        };

        public enum Sampling : byte
        {
            Unspecified = 0x00,
            Instantaneous = 0x01,
            ArithmeticMean = 0x02,
            RMS = 0x03,
            Maximum = 0x04,
            Minimum = 0x05,
            Accumulated = 0x06,
            Count = 0x07
        };

        private struct sensorItem
        {
            public SensorType sensorType;
            public GattLocalCharacteristic sensorChar;
            public Buffer dataBuffer;
        };

        public HidService(GattServiceProvider provider)
        {
            _service = provider.AddService(GattServiceUuids.HumanInterfaceDevice);

            // HID information characteristic
            _service.CreateCharacteristic(GattCharacteristicUuids.HidInformation, new GattLocalCharacteristicParameters()
            {
                UserDescription = "Hid information",
                CharacteristicProperties = GattCharacteristicProperties.Read,
                StaticValue = new Buffer(new byte[] { 0x11, 0x1, 0x00, 0x02 })
            });

            // reportmap characteristic
            _service.CreateCharacteristic(GattCharacteristicUuids.ReportMap, new GattLocalCharacteristicParameters()
            {
                UserDescription = "reportmap",
                CharacteristicProperties = GattCharacteristicProperties.Read,
                StaticValue = new Buffer(_hidReportValue),
            });

            // hid control characteristic
            _service.CreateCharacteristic(GattCharacteristicUuids.HidControlPoint, new GattLocalCharacteristicParameters()
            {
               UserDescription = "hid control characteristic",
               CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse
            });

            // protocol mode characteristic
            _service.CreateCharacteristic(GattCharacteristicUuids.ProtocolMode, new GattLocalCharacteristicParameters()
            {
                UserDescription = "protocol mode characteristic",
                CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse | GattCharacteristicProperties.WriteWithoutResponse,
            });

            // input report
            GattLocalCharacteristicResult result = _service.CreateCharacteristic(GattCharacteristicUuids.Report, new GattLocalCharacteristicParameters()
            {
                UserDescription = "input report characteristic",
                CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,

            });
            _inputReportCharacteristic = result.Characteristic;
            _inputReportCharacteristic.CreateDescriptor(GattDescriptorUuids.ClientCharacteristicConfiguration, new GattLocalDescriptorParameters()
            {
                ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
                WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });
            _inputReportCharacteristic.CreateDescriptor(Utilities.CreateUuidFromShortCode(0x2908), new GattLocalDescriptorParameters()
            {
                ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
                WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
                StaticValue = new Buffer(new byte[] {0x00, 0x01}),
            });
        }

        /// <summary>
        /// Add the sensor to the Environmental Sensor Service.
        /// </summary>
        /// <param name="sType">Type of Sensor</param>
        /// <param name="description">Description / location</param>
        /// <param name="sampling">Sampling function</param>
        /// <returns></returns>
        public int AddSensor(SensorType sType, string description, Sampling sampling = Sampling.Unspecified)
        {
            GattLocalCharacteristicResult result =
                    _service.CreateCharacteristic(GetUuidForType(sType), new GattLocalCharacteristicParameters()
                    {
                        UserDescription = description,
                        CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify
                    });

            GattLocalCharacteristic sensor = result.Characteristic;
            sensor.ReadRequested += Sensor_ReadRequested;

            // Add descriptors
            AddMeasurementDescriptor(sensor, sampling, 0);

            sensorItem si = new() { sensorType = sType, sensorChar = sensor, dataBuffer = null };

            return _sensors.Add(si);
        }

        /// <summary>
        /// Update the Sensor value. If any device is subscribed to this sensor 
        /// it will be notified.
        /// </summary>
        /// <param name="sensorIndex">Index number to sensor returned from AddSensor()</param>
        /// <param name="value">New value for sensor</param>
        public void UpdateValue(int sensorIndex, float value)
        {
            bool updated = false;

            // Let it throw exception if invalid index
            sensorItem si = (sensorItem)_sensors[sensorIndex];

            DataWriter writer = new();
            switch (si.sensorType)
            {
                case SensorType.Temperature:
                    // Temperature in Celsius
                    // uint16 - hundreds of C, 9543 = 95.43c
                    Int16 temp = (Int16)(value * 100);
                    writer.WriteInt16(temp);                  // Temperature 
                    updated = true;
                    break;

                case SensorType.Humidity:
                    // Humidity percentage
                    // uint16 - hundreds of %, 9543 = 95.43%
                    UInt16 humidity = (UInt16)(value * 100);
                    writer.WriteUInt16(humidity);
                    updated = true;
                    break;

                case SensorType.Pressure:
                    // Pressure Pascal
                    UInt32 pa = (UInt32)(value * 10);
                    writer.WriteUInt32(pa);
                    updated = true;
                    break;

                case SensorType.Rainfall:
                    // Rainfall in mm
                    writer.WriteUInt16((UInt16)value);
                    updated = true;
                    break;
            }

            if (updated)
            {
                si.dataBuffer = writer.DetachBuffer();
                _sensors[sensorIndex] = si;

                si.sensorChar.NotifyValue(si.dataBuffer);
            }
        }

        private void Sensor_ReadRequested(GattLocalCharacteristic sender, GattReadRequestedEventArgs ReadRequestEventArgs)
        {
            GattReadRequest request = ReadRequestEventArgs.GetRequest();

            foreach (sensorItem si in _sensors)
            {
                if (si.sensorChar == sender && si.dataBuffer != null)
                {
                    request.RespondWithValue(si.dataBuffer);
                    return;
                }
            }

            request.RespondWithProtocolError(GattProtocolError.AttributeNotFound);
        }

        private Guid GetUuidForType(SensorType type)
        {
            switch (type)
            {
                case SensorType.Temperature:
                    return GattCharacteristicUuids.Temperature;

                case SensorType.Humidity:
                    return GattCharacteristicUuids.Humidity;

                case SensorType.Pressure:
                    return GattCharacteristicUuids.Pressure;

                case SensorType.Rainfall:
                    return GattCharacteristicUuids.Rainfall;
            }

            throw new ArgumentOutOfRangeException();
        }

        private void AddMeasurementDescriptor(GattLocalCharacteristic sensor, Sampling sampleFunction, byte application)
        {
            // Set up Descriptors - ESS Measurement
            DataWriter essmWriter = new();
            essmWriter.WriteInt16(0);       // Flags reserved
            essmWriter.WriteByte((byte)sampleFunction);        // Sampling -Unspecified
            essmWriter.WriteBytes(new byte[3] { 0, 0, 0 }); // Int24 - Measurement period - 0 Not in use
            essmWriter.WriteBytes(new byte[3] { 0, 0, 0 }); // Int24 - Update period - 0 Not in use
            essmWriter.WriteByte(application);        // Application - 0 = Unspecified
            essmWriter.WriteByte(0xff);     // Measurement Uncertainty - 0xff = Information not available 

            sensor.CreateDescriptor(GattDescriptorUuids.EssMeasurement, new GattLocalDescriptorParameters
            {
                StaticValue = essmWriter.DetachBuffer()
            });
        }

        private byte[] _hidReportValue = new byte[]{
          HidType.USAGE_PAGE(1),       0x01, //     USAGE_PAGE (Generic Desktop)
          HidType.USAGE(1),            0x02, //     USAGE (Mouse)
          HidType.COLLECTION(1),       0x01, //     COLLECTION (Application)
          HidType.USAGE(1),            0x01, //     USAGE (Pointer)
          HidType.COLLECTION(1),       0x00, //     COLLECTION (Physical)
          // ------------------------------------------------- Buttons (Left, Right, Middle, Back, Forward)
          HidType.USAGE_PAGE(1),       0x09, //     USAGE_PAGE (Button)
          HidType.USAGE_MINIMUM(1),    0x01, //     USAGE_MINIMUM (Button 1)
          HidType.USAGE_MAXIMUM(1),    0x05, //     USAGE_MAXIMUM (Button 5)
          HidType.LOGICAL_MINIMUM(1),  0x00, //     LOGICAL_MINIMUM (0)
          HidType.LOGICAL_MAXIMUM(1),  0x01, //     LOGICAL_MAXIMUM (1)
          HidType.REPORT_SIZE(1),      0x01, //     REPORT_SIZE (1)
          HidType.REPORT_COUNT(1),     0x05, //     REPORT_COUNT (5)
          HidType.HIDINPUT(1),         0x02, //     INPUT (Data, Variable, Absolute) ;5 button bits
          // ------------------------------------------------- Padding
          HidType.REPORT_SIZE(1),      0x03, //     REPORT_SIZE (3)
          HidType.REPORT_COUNT(1),     0x01, //     REPORT_COUNT (1)
          HidType.HIDINPUT(1),         0x03, //     INPUT (Constant, Variable, Absolute) ;3 bit padding
          // ------------------------------------------------- X/Y position, Wheel
          HidType.USAGE_PAGE(1),       0x01, //     USAGE_PAGE (Generic Desktop)
          HidType.USAGE(1),            0x30, //     USAGE (X)
          HidType.USAGE(1),            0x31, //     USAGE (Y)
          HidType.USAGE(1),            0x38, //     USAGE (Wheel)
          HidType.LOGICAL_MINIMUM(1),  0x81, //     LOGICAL_MINIMUM (-127)
          HidType.LOGICAL_MAXIMUM(1),  0x7f, //     LOGICAL_MAXIMUM (127)
          HidType.REPORT_SIZE(1),      0x08, //     REPORT_SIZE (8)
          HidType.REPORT_COUNT(1),     0x03, //     REPORT_COUNT (3)
          HidType.HIDINPUT(1),         0x06, //     INPUT (Data, Variable, Relative) ;3 bytes (X,Y,Wheel)
          // ------------------------------------------------- Horizontal wheel
          HidType.USAGE_PAGE(1),       0x0c, //     USAGE PAGE (Consumer Devices)
          HidType.USAGE(2),      0x38, 0x02, //     USAGE (AC Pan)
          HidType.LOGICAL_MINIMUM(1),  0x81, //     LOGICAL_MINIMUM (-127)
          HidType.LOGICAL_MAXIMUM(1),  0x7f, //     LOGICAL_MAXIMUM (127)
          HidType.REPORT_SIZE(1),      0x08, //     REPORT_SIZE (8)
          HidType.REPORT_COUNT(1),     0x01, //     REPORT_COUNT (1)
          HidType.HIDINPUT(1),         0x06, //     INPUT (Data, Var, Rel)
          HidType.END_COLLECTION(0),         //     END_COLLECTION
          HidType.END_COLLECTION(0)          //     END_COLLECTION
        };

        private class HidType
        {
            // byte HID_VERSION_1_11 = (0x0111);

            /* HID Class */
            public static byte HID_CLASS = (3);
            public static byte HID_SUBCLASS_NONE = (0);
            public static byte HID_PROTOCOL_NONE = (0);

            /* Descriptors */
            public static byte HID_DESCRIPTOR = (33);
            public static byte HID_DESCRIPTOR_LENGTH = (0x09);
            public static byte REPORT_DESCRIPTOR = (34);

            /* Class requests */
            public static byte GET_REPORT = (0x1);
            public static byte GET_IDLE = (0x2);
            public static byte SET_REPORT = (0x9);
            public static byte SET_IDLE = (0xa);

            /* HID Class Report Descriptor */
            /* Short items: size is 0, 1, 2 or 3 specifying 0, 1, 2 or 4 (four) bytes */
            /* of data as per HID Class standard */

            /* Main items */
            public static byte HIDINPUT(byte size) => (byte)(0x80 | size);
            public static byte HIDOUTPUT(byte size) => (byte)(0x90 | size);
            public static byte FEATURE(byte size) => (byte)(0xb0 | size);
            public static byte COLLECTION(byte size) => (byte)(0xa0 | size);
            public static byte END_COLLECTION(byte size) => (byte)(0xc0 | size);

            /* Global items */
            public static byte USAGE_PAGE(byte size) => (byte)(0x04 | size);
            public static byte LOGICAL_MINIMUM(byte size) => (byte)(0x14 | size);
            public static byte LOGICAL_MAXIMUM(byte size) => (byte)(0x24 | size);
            public static byte PHYSICAL_MINIMUM(byte size) => (byte)(0x34 | size);
            public static byte PHYSICAL_MAXIMUM(byte size) => (byte)(0x44 | size);
            public static byte UNIT_EXPONENT(byte size) => (byte)(0x54 | size);
            public static byte UNIT(byte size) => (byte)(0x64 | size);
            public static byte REPORT_SIZE(byte size) => (byte)(0x74 | size);  //bits
            public static byte REPORT_ID(byte size) => (byte)(0x84 | size);
            public static byte REPORT_COUNT(byte size) => (byte)(0x94 | size);  //bytes
            public static byte PUSH(byte size) => (byte)(0xa4 | size);
            public static byte POP(byte size) => (byte)(0xb4 | size);

            /* Local items */
            public static byte USAGE(byte size) => (byte)(0x08 | size);
            public static byte USAGE_MINIMUM(byte size) => (byte)(0x18 | size);
            public static byte USAGE_MAXIMUM(byte size) => (byte)(0x28 | size);
            public static byte DESIGNATOR_INDEX(byte size) => (byte)(0x38 | size);
            public static byte DESIGNATOR_MINIMUM(byte size) => (byte)(0x48 | size);
            public static byte DESIGNATOR_MAXIMUM(byte size) => (byte)(0x58 | size);
            public static byte STRING_INDEX(byte size) => (byte)(0x78 | size);
            public static byte STRING_MINIMUM(byte size) => (byte)(0x88 | size);
            public static byte STRING_MAXIMUM(byte size) => (byte)(0x98 | size);
            public static byte DELIMITER(byte size) => (byte)(0xa8 | size);

            /* HID Report */
            /* Where report IDs are used the first byte of 'data' will be the */
            /* report ID and 'length' will include this report ID byte. */

            public static byte MAX_HID_REPORT_SIZE = (64);
        }
    }
}
