using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlphaOmega.IO;
using NUnit.Framework;

namespace NamedPipesProxy.Tests
{
	[TestFixture]
	[Timeout(5000)]
	public class PipeMessageTests
	{
		private class TestPayload
		{
			public String Name { get; set; }
			public Int32 Value { get; set; }
		}

		[Test]
		public void Constructor_WithMethodNameAndPayload_SetsProperties()
		{
			String type = "TestType";
			TestPayload payload = new TestPayload { Name = "Alpha", Value = 42 };

			PipeMessage message = new PipeMessage(type, payload);

			Assert.AreEqual(type, message.Type);
			Assert.IsNotNull(message.Payload);
			TestPayload deserialized = message.Deserialize<TestPayload>();
			Assert.AreEqual(payload.Name, deserialized.Name);
			Assert.AreEqual(payload.Value, deserialized.Value);
		}

		[Test]
		public void Constructor_CopyRequestIdsAndPayload_SetsProperties()
		{
			PipeMessage original = new PipeMessage("Type1", new TestPayload { Name = "A", Value = 1 });
			Guid reqId = original.RequestId;
			Guid msgId = original.MessageId;

			PipeMessage copy = new PipeMessage(original, "Type2", new TestPayload { Name = "B", Value = 2 });

			Assert.AreEqual(reqId, copy.RequestId);
			Assert.AreEqual(msgId, copy.MessageId);
			Assert.AreEqual("Type2", copy.Type);
			TestPayload deserialized = copy.Deserialize<TestPayload>();
			Assert.AreEqual("B", deserialized.Name);
			Assert.AreEqual(2, deserialized.Value);
		}

		[Test]
		public void Constructor_CopyTypePayloadRequestId_SetsProperties()
		{
			PipeMessage original = new PipeMessage("Type1", new TestPayload { Name = "A", Value = 1 });
			PipeMessage copy = new PipeMessage(original);

			Assert.AreEqual(original.Type, copy.Type);
			Assert.AreEqual(original.RequestId, copy.RequestId);
			Assert.AreEqual(original.Payload, copy.Payload);
		}

		[Test]
		public void Deserialize_Generic_ReturnsCorrectObject()
		{
			TestPayload payload = new TestPayload { Name = "Test", Value = 99 };
			PipeMessage message = new PipeMessage("Type", payload);
			TestPayload result = message.Deserialize<TestPayload>();
			Assert.AreEqual(payload.Name, result.Name);
			Assert.AreEqual(payload.Value, result.Value);
		}

		[Test]
		public void Deserialize_Type_ReturnsCorrectObject()
		{
			TestPayload payload = new TestPayload { Name = "Test", Value = 99 };
			PipeMessage message = new PipeMessage("Type", payload);
			Object result = message.Deserialize(typeof(TestPayload));
			Assert.IsInstanceOf<TestPayload>(result);
			Assert.AreEqual(payload.Name, ((TestPayload)result).Name);
			Assert.AreEqual(payload.Value, ((TestPayload)result).Value);
		}

		[Test]
		public void Deserialize_TypeArray_ReturnsCorrectObjects()
		{
			Object[] payload = { "str", 123 };
			PipeMessage message = new PipeMessage("Type", payload);
			Object[] result = message.Deserialize(new Type[] { typeof(String), typeof(Int32) });
			Assert.AreEqual("str", result[0]);
			Assert.AreEqual(123, result[1]);
		}

		[Test]
		public void Serialize_And_Deserialize_Static_Works()
		{
			TestPayload payload = new TestPayload { Name = "Static", Value = 7 };
			Byte[] bytes = PipeMessage.Serialize(payload);
			TestPayload result = PipeMessage.Deserialize<TestPayload>(bytes);
			Assert.AreEqual(payload.Name, result.Name);
			Assert.AreEqual(payload.Value, result.Value);
		}

		[Test]
		public void ToString_ReturnsExpectedFormat()
		{
			TestPayload payload = new TestPayload { Name = "Alpha", Value = 1 };
			PipeMessage message = new PipeMessage("Type", payload);
			String str = message.ToString();
			Assert.IsTrue(str.Contains("Type"));
			Assert.IsTrue(str.Contains("RequestId"));
			Assert.IsTrue(str.Contains("MessageId"));
			Assert.IsTrue(str.Contains("Payload"));
		}

		[Test]
		public async Task WriteToStream_And_ReadFromStream_Roundtrip()
		{
			TestPayload payload = new TestPayload { Name = "Stream", Value = 123 };
			PipeMessage message = new PipeMessage("Type", payload);
			using(MemoryStream ms = new MemoryStream())
			{
				CancellationToken token = CancellationToken.None;
				await message.WriteToStream(ms, token);
				ms.Position = 0;
				PipeMessage result = await PipeMessage.ReadFromStream(ms, token);
				Assert.AreEqual(message.Type, result.Type);
				TestPayload resultPayload = result.Deserialize<TestPayload>();
				Assert.AreEqual(payload.Name, resultPayload.Name);
				Assert.AreEqual(payload.Value, resultPayload.Value);
			}
		}

		[Test]
		public void Deserialize_InvalidPayload_Throws()
		{
			PipeMessage message = new PipeMessage("Type", "not a number");
			Assert.Throws<Newtonsoft.Json.JsonReaderException>(() => message.Deserialize(typeof(Int32)));
		}

		[Test]
		public void Deserialize_TypeArray_MismatchedCount_Throws()
		{
			Object[] payload = { "str" };
			PipeMessage message = new PipeMessage("Type", payload);
			Assert.Throws<InvalidOperationException>(() => message.Deserialize(new Type[] { typeof(String), typeof(Int32) }));
		}
	}
}
