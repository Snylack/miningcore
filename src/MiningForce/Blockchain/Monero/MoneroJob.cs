﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using CodeContracts;
using MiningForce.Blockchain.Monero.DaemonResponses;
using MiningForce.Configuration;
using MiningForce.Crypto;
using MiningForce.Crypto.Hashing;
using MiningForce.Crypto.Hashing.Algorithms;
using MiningForce.Extensions;
using MiningForce.Stratum;
using NBitcoin.BouncyCastle.Math;

namespace MiningForce.Blockchain.Monero
{
	public class MoneroJob
	{
		public MoneroJob(GetBlockTemplateResponse blockTemplate, byte[] instanceId, string jobId,
			PoolConfig poolConfig, ClusterConfig clusterConfig,
			MoneroNetworkType networkType)
		{
			Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
			Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
			Contract.RequiresNonNull(instanceId, nameof(instanceId));
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

			//blockTemplate = JsonConvert.DeserializeObject<GetBlockTemplateResult>("{\"capabilities\":[\"proposal\"],\"version\":536870912,\"rules\":[\"csv\",\"segwit\"],\"vbavailable\":{},\"vbrequired\":0,\"previousblockhash\":\"33e9ec25751d0d5ad43660d925271923649e4dc675449a29342087ee26227ac4\",\"transactions\":[{\"data\":\"0100000001f209942b305d7e1ff4d4017f8e4a82946e35ab943d98049580a5ef446741c07f010000006a47304402201735be63bf6527f3a419eccd8dddd2a68bd652170a304b95a1f76e3a9a310908022044c86f2e3238aa8ec960206d5395fcbe69fa61a2671895fda2142474d01ecf280121039be344e1dc60c08355f4ba7cd3c7e0a57873aba19e782d281143a4ada0481663feffffff02002d3101000000001976a9147b108ab6bbffc56a6c1ce0d14edad1a588aac9f088ac5610ba17000000001976a9146f63066c9f43becbd89a2c5ef32268ef07b8907b88ac68ed0100\",\"txid\":\"dd3a70fceb9e792fbd3f0cabdaba31edf46dc18937e0ff200e67a3531a6e5014\",\"hash\":\"dd3a70fceb9e792fbd3f0cabdaba31edf46dc18937e0ff200e67a3531a6e5014\",\"depends\":[],\"fee\":22600,\"sigops\":8,\"weight\":900},{\"data\":\"01000000020749c01b88bbb998ea2bb829d2fd1509e064a865be25584ebb7d238c2d2c7ce6000000006b483045022100e1a30aec16addf31111acb977dfbbe8747a43b4fd810a6c53a4622be409e8921022024273c724935cf00bece0e07bdf48e2ca2144cd45805689b2ca801fbbe919bb20121031b392bcd589e62244a4d1529f1e21aba9defb0ffa71c5e2e14e22f3f5081e00afeffffffc6fba4e75b294a488c62b41bc02ebcfb9d33c1842960ef2c456676112cc134c7000000006b483045022100df744f016524b15a73d3f603613a4f8cb58bf082a10c1602d0a0f6f8c06de71a0220134babf4b34582d3fbcfbf0b70092a995c356c66053b767a6bfd684537ba66760121033a6da8e5dd47967df138f6d32d834571db1b90c5f4f531683078550aa98038dafeffffff02f3821100000000001976a914b52631c6125289fe5d59319c39c149aac2bc8b9b88ac40b9ec05000000001976a9145ea4e971f6007878ee2215ac3dbadae13b41585988ac68ed0100\",\"txid\":\"7191c509b371ad24a43259cd3416e61b66647686f0a97c34fb289274ccc56132\",\"hash\":\"7191c509b371ad24a43259cd3416e61b66647686f0a97c34fb289274ccc56132\",\"depends\":[],\"fee\":37400,\"sigops\":8,\"weight\":1496}],\"coinbaseaux\":{\"flags\":\"\"},\"coinbasevalue\":5000060000,\"longpollid\":\"33e9ec25751d0d5ad43660d925271923649e4dc675449a29342087ee26227ac455260\",\"target\":\"0000003713130000000000000000000000000000000000000000000000000000\",\"mintime\":1501243676,\"mutable\":[\"time\",\"transactions\",\"prevblock\"],\"noncerange\":\"00000000ffffffff\",\"sigoplimit\":80000,\"sizelimit\":4000000,\"weightlimit\":4000000,\"curtime\":1501245752,\"bits\":\"1d371313\",\"height\":126313,\"default_witness_commitment\":\"6a24aa21a9edc0669f40a7281cb4f8c88e35f084319af5408fe75fba1458c5ccf871a984c887\"}");

			this.poolConfig = poolConfig;
			this.clusterConfig = clusterConfig;
			this.networkType = networkType;
			this.blockTemplate = blockTemplate;

			PrepareBlobTemplate(instanceId);
		}

		private readonly ClusterConfig clusterConfig;
		private readonly PoolConfig poolConfig;
		private readonly GetBlockTemplateResponse blockTemplate;
		private byte[] blobTemplate;
		private readonly MoneroNetworkType networkType;
		private uint extraNonce = 0;

		#region API-Surface

		public GetBlockTemplateResponse BlockTemplate => blockTemplate;

		public void Init()
		{
		}

		public void PrepareWorkerJob(MoneroWorkerJob workerJob, out string blob, out string target)
		{
			workerJob.Height = blockTemplate.Height;
			workerJob.ExtraNonce = ++extraNonce;

			blob = EncodeBlob(workerJob.ExtraNonce);
			target = EncodeTarget(workerJob.Difficulty);
		}

		public MoneroShare ProcessShare(string nonce, uint workerExtraNonce, string workerHash, double stratumDifficulty)
		{
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(workerHash), $"{nameof(workerHash)} must not be empty");
			Contract.Requires<ArgumentException>(extraNonce != 0, $"{nameof(extraNonce)} must not be empty");

			// validate nonce
			if (!MoneroConstants.RegexValidNonce.IsMatch(nonce))
				throw new StratumException(StratumError.MinusOne, "malformed nonce");

			// clone template
			var blob = new byte[blobTemplate.Length];
			Buffer.BlockCopy(blobTemplate, 0, blob, 0, blobTemplate.Length);

			// inject extranonce
			var extraNonceBytes = BitConverter.GetBytes(workerExtraNonce.ToBigEndian());
			Buffer.BlockCopy(extraNonceBytes, 0, blob, (int)blockTemplate.ReservedOffset, extraNonceBytes.Length);

			// inject nonce
			var nonceBytes = nonce.HexToByteArray();
			Buffer.BlockCopy(nonceBytes, 0, blob, MoneroConstants.BlobNonceOffset, nonceBytes.Length);

			// convert
			var converted = LibCryptoNote.ConvertBlob(blob);
			if(converted == null)
				throw new StratumException(StratumError.MinusOne, "malformed blob");

			// hash it
			var hashBytes = LibCryptoNote.CryptonightHashSlow(converted);
			var hash = hashBytes.ToHexString();

			if (hash != workerHash)
				throw new StratumException(StratumError.MinusOne, "bad hash");

			// check difficulty
			var hashDiff = MoneroConstants.Diff1.Divide(new BigInteger(hashBytes.ToReverseArray()));
			var hashDiffLong = hashDiff.LongValue;

			// test if share meets at least workers current difficulty
			var ratio = hashDiffLong / stratumDifficulty;

			if (ratio < 0.99)
				throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({hashDiffLong})");

			// valid share
			var result = new MoneroShare
			{
				Difficulty = hashDiffLong,
				NormalizedDifficulty = hashDiffLong,
				StratumDifficulty = stratumDifficulty,
				BlockHeight = blockTemplate.Height
			};

			// now check if the share meets the much harder block difficulty (block candidate)
			if (hashDiff.LongValue >= blockTemplate.Difficulty)
			{
				result.IsBlockCandidate = true;
				result.BlobHex = blob.ToHexString();
				result.BlobHash = LibCryptoNote.CryptonightHashFast(converted).ToHexString();
			}

			return result;
		}

		#endregion // API-Surface

		private void PrepareBlobTemplate(byte[] instanceId)
		{
			blobTemplate = blockTemplate.Blob.HexToByteArray();

			// inject instanceId at the end of the reserved area of the blob
			var destOffset = (int) blockTemplate.ReservedOffset + MoneroConstants.ExtraNonceSize;
			Buffer.BlockCopy(instanceId, 0, blobTemplate, destOffset, 3);
		}

		private string EncodeBlob(uint workerExtraNonce)
		{
			// clone template
			var blob = new byte[blobTemplate.Length];
			Buffer.BlockCopy(blobTemplate, 0, blob, 0, blobTemplate.Length);

			// inject extranonce (big-endian at the beginning of the reserved area of the blob)
			var extraNonceBytes = BitConverter.GetBytes(workerExtraNonce.ToBigEndian());
			Buffer.BlockCopy(extraNonceBytes, 0, blob, (int) blockTemplate.ReservedOffset, extraNonceBytes.Length);

			var result = LibCryptoNote.ConvertBlob(blob).ToHexString();
			return result;
		}

		private string EncodeTarget(double difficulty)
		{
			var diff = BigInteger.ValueOf((long) difficulty);
			var quotient = MoneroConstants.Diff1.Divide(diff);
			var bytes = quotient.ToByteArray();
			var padded = Enumerable.Repeat((byte) 0, 32).ToArray();

			Buffer.BlockCopy(bytes, 0, padded, padded.Length - bytes.Length, bytes.Length);

			var result = new ArraySegment<byte>(padded, 0, 4)
				.Reverse()
				.ToHexString();

			return result;
		}

		private ShareBase ProcessShareInternal(string extraNonce1, string extraNonce2, uint nonce, double stratumDifficulty)
		{
			// valid share
			var result = new ShareBase
			{
			};

			return result;
		}
	}
}
