﻿using System;
using System.Threading.Tasks;
using MiningForce.Configuration;
using MiningForce.Stratum;

namespace MiningForce.Blockchain
{
    public class NetworkStats
    {
        public string Network { get; set; }
        public double HashRate { get; set; }
        public DateTime? LastBlockTime { get; set; }
        public double Difficulty { get; set; }
        public int BlockHeight { get; set; }
        public int ConnectedPeers { get; set; }
        public string RewardType { get; set; }
    }

	public interface IShare
	{
		/// <summary>
		/// Who mined it
		/// </summary>
		string Worker { get; }

		/// <summary>
		/// From where was it submitted
		/// </summary>
		string IpAddress { get; }

		/// <summary>
		/// Share difficulty
		/// </summary>
		double Difficulty { get; set; }

		/// <summary>
		/// Difficulty relative to a Bitcoin Difficulty 1 Share (used for pool hashrate calculation)
		/// </summary>
		double DifficultyNormalized { get; set; }

		/// <summary>
		/// Block this share refers to
		/// </summary>
		long BlockHeight { get; set; }

		/// <summary>
		/// Coin
		/// </summary>
		CoinType Coin { get; set; }

		/// <summary>
		/// If this share presumably resulted in a block
		/// </summary>
		bool IsBlockCandidate { get; set; }

		/// <summary>
		/// Arbitrary data to be interpreted by the payment processor specialized 
		/// in this coin to verify this block candidate was accepted by the network
		/// </summary>
		string TransactionConfirmationData { get; set; }
	}

	public interface IBlockchainJobManager
    {
        Task StartAsync(StratumServer stratum);
        Task<bool> ValidateAddressAsync(string address);
        Task<object[]> SubscribeWorkerAsync(StratumClient worker);
        Task<bool> AuthenticateWorkerAsync(StratumClient worker, string workername, string password);
        Task<IShare> SubmitShareAsync(StratumClient worker, object submission, double stratumDifficulty);

        IObservable<object> Jobs { get; }
        NetworkStats NetworkStats { get; }
    }
}