using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Helpers;
using WalletWasabi.Hwi2.Exceptions;
using WalletWasabi.Hwi2.Models;
using WalletWasabi.Hwi2.Parsers;
using WalletWasabi.Hwi2.ProcessBridge;
using WalletWasabi.Interfaces;
using WalletWasabi.KeyManagement;

namespace WalletWasabi.Hwi2
{
	public class HwiClient
	{
		#region PropertiesAndMembers

		public Network Network { get; }
		public IProcessBridge Bridge { get; }

		#endregion PropertiesAndMembers

		#region ConstructorsAndInitializers

		public HwiClient(Network network, IProcessBridge bridge = null)
		{
			Network = Guard.NotNull(nameof(network), network);
			Bridge = bridge ?? new HwiProcessBridge();
		}

		#endregion ConstructorsAndInitializers

		#region Commands

		private async Task<string> SendCommandAsync(IEnumerable<HwiOption> options, HwiCommands? command, string commandArguments, bool openConsole, CancellationToken cancel)
		{
			string arguments = HwiParser.ToArgumentString(Network, options, command, commandArguments);

			try
			{
				(string responseString, int exitCode) = await Bridge.SendCommandAsync(arguments, openConsole, cancel).ConfigureAwait(false);

				if (exitCode != 0)
				{
					ThrowIfError(responseString);
					throw new HwiException(HwiErrorCode.UnknownError, $"'hwi {arguments}' exited with incorrect exit code: {exitCode}.");
				}

				ThrowIfError(responseString);

				if (JsonHelpers.TryParseJToken(responseString, out JToken responseJToken))
				{
					return responseString;
				}
				else
				{
					return responseString;
				}
			}
			catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException || ex is TimeoutException)
			{
				throw new OperationCanceledException($"'hwi {arguments}' operation is canceled.", ex);
			}
		}

		public async Task PromptPinAsync(HardwareWalletVendors deviceType, string devicePath, CancellationToken cancel)
			=> await PromptPinImplAsync(deviceType, devicePath, null, cancel);

		public async Task PromptPinAsync(HDFingerprint fingerprint, CancellationToken cancel)
			=> await PromptPinImplAsync(null, null, fingerprint, cancel);

		private async Task PromptPinImplAsync(HardwareWalletVendors? deviceType, string devicePath, HDFingerprint? fingerprint, CancellationToken cancel)
		{
			await SendCommandAsync(
				options: BuildOptions(deviceType, devicePath, fingerprint),
				command: HwiCommands.PromptPin,
				commandArguments: null,
				openConsole: false,
				cancel).ConfigureAwait(false);
		}

		public async Task SendPinAsync(HardwareWalletVendors deviceType, string devicePath, int pin, CancellationToken cancel)
			=> await SendPinImplAsync(deviceType, devicePath, null, pin, cancel);

		public async Task SendPinAsync(HDFingerprint fingerprint, int pin, CancellationToken cancel)
			=> await SendPinImplAsync(null, null, fingerprint, pin, cancel);

		private async Task SendPinImplAsync(HardwareWalletVendors? deviceType, string devicePath, HDFingerprint? fingerprint, int pin, CancellationToken cancel)
		{
			await SendCommandAsync(
				options: BuildOptions(deviceType, devicePath, fingerprint),
				command: HwiCommands.SendPin,
				commandArguments: pin.ToString(),
				openConsole: false,
				cancel).ConfigureAwait(false);
		}

		public async Task<ExtPubKey> GetXpubAsync(HardwareWalletVendors deviceType, string devicePath, KeyPath keyPath, CancellationToken cancel)
			=> await GetXpubImplAsync(deviceType, devicePath, null, keyPath, cancel);

		public async Task<ExtPubKey> GetXpubAsync(HDFingerprint fingerprint, KeyPath keyPath, CancellationToken cancel)
			=> await GetXpubImplAsync(null, null, fingerprint, keyPath, cancel);

		private async Task<ExtPubKey> GetXpubImplAsync(HardwareWalletVendors? deviceType, string devicePath, HDFingerprint? fingerprint, KeyPath keyPath, CancellationToken cancel)
		{
			string keyPathString = keyPath.ToString(true, "h");
			var response = await SendCommandAsync(
				options: BuildOptions(deviceType, devicePath, fingerprint),
				command: HwiCommands.GetXpub,
				commandArguments: keyPathString,
				openConsole: false,
				cancel).ConfigureAwait(false);

			var extPubKey = HwiParser.ParseExtPubKey(response);

			return extPubKey;
		}

		public async Task<BitcoinWitPubKeyAddress> DisplayAddressAsync(HardwareWalletVendors deviceType, string devicePath, KeyPath keyPath, CancellationToken cancel)
			=> await DisplayAddressImplAsync(deviceType, devicePath, null, keyPath, cancel);

		public async Task<BitcoinWitPubKeyAddress> DisplayAddressAsync(HDFingerprint fingerprint, KeyPath keyPath, CancellationToken cancel)
			=> await DisplayAddressImplAsync(null, null, fingerprint, keyPath, cancel);

		private async Task<BitcoinWitPubKeyAddress> DisplayAddressImplAsync(HardwareWalletVendors? deviceType, string devicePath, HDFingerprint? fingerprint, KeyPath keyPath, CancellationToken cancel)
		{
			var response = await SendCommandAsync(
				options: BuildOptions(deviceType, devicePath, fingerprint),
				command: HwiCommands.DisplayAddress,
				commandArguments: $"--path {keyPath.ToString(true, "h")} --wpkh",
				openConsole: false,
				cancel).ConfigureAwait(false);

			var address = HwiParser.ParseAddress(response, Network) as BitcoinWitPubKeyAddress;

			address = address.TransformToNetworkNetwork(Network);

			return address as BitcoinWitPubKeyAddress;
		}

		public async Task<PSBT> SignTxAsync(HardwareWalletVendors deviceType, string devicePath, PSBT psbt, CancellationToken cancel)
			=> await SignTxImplAsync(deviceType, devicePath, null, psbt, cancel);

		public async Task<PSBT> SignTxAsync(HDFingerprint fingerprint, PSBT psbt, CancellationToken cancel)
			=> await SignTxImplAsync(null, null, fingerprint, psbt, cancel);

		private async Task<PSBT> SignTxImplAsync(HardwareWalletVendors? deviceType, string devicePath, HDFingerprint? fingerprint, PSBT psbt, CancellationToken cancel)
		{
			var psbtString = psbt.ToBase64();

			var response = await SendCommandAsync(
				options: BuildOptions(deviceType, devicePath, fingerprint),
				command: HwiCommands.SignTx,
				commandArguments: psbtString,
				openConsole: false,
				cancel).ConfigureAwait(false);

			PSBT signedPsbt = HwiParser.ParsePsbt(response, Network);

			if (!signedPsbt.IsAllFinalized())
			{
				signedPsbt.Finalize();
			}

			return signedPsbt;
		}

		public async Task WipeAsync(HardwareWalletVendors deviceType, string devicePath, CancellationToken cancel)
		{
			await SendCommandAsync(
				options: BuildOptions(deviceType, devicePath, null),
				command: HwiCommands.Wipe,
				commandArguments: null,
				openConsole: false,
				cancel).ConfigureAwait(false);
		}

		public async Task SetupAsync(HardwareWalletVendors deviceType, string devicePath, bool openConsole, CancellationToken cancel)
		{
			await SendCommandAsync(
				options: BuildOptions(deviceType, devicePath, null, HwiOption.Interactive),
				command: HwiCommands.Setup,
				commandArguments: null,
				openConsole: openConsole,
				cancel).ConfigureAwait(false);
		}

		public async Task RestoreAsync(HardwareWalletVendors deviceType, string devicePath, bool openConsole, CancellationToken cancel)
		{
			await SendCommandAsync(
				options: BuildOptions(deviceType, devicePath, null, HwiOption.Interactive),
				command: HwiCommands.Restore,
				commandArguments: null,
				openConsole: openConsole,
				cancel).ConfigureAwait(false);
		}

		public async Task<Version> GetVersionAsync(CancellationToken cancel)
		{
			string responseString = await SendCommandAsync(
				options: new[] { HwiOption.Version },
				command: null,
				commandArguments: null,
				openConsole: false,
				cancel).ConfigureAwait(false);

			var version = HwiParser.ParseVersion(responseString);
			return version;
		}

		public async Task<string> GetHelpAsync(CancellationToken cancel)
		{
			string responseString = await SendCommandAsync(
				options: new[] { HwiOption.Help },
				command: null,
				commandArguments: null,
				openConsole: false,
				cancel).ConfigureAwait(false);

			return responseString;
		}

		public async Task<IEnumerable<HwiEnumerateEntry>> EnumerateAsync(CancellationToken cancel)
		{
			string responseString = await SendCommandAsync(
				options: null,
				command: HwiCommands.Enumerate,
				commandArguments: null,
				openConsole: false,
				cancel).ConfigureAwait(false);
			IEnumerable<HwiEnumerateEntry> response = HwiParser.ParseHwiEnumerateResponse(responseString);

			return response;
		}

		#endregion Commands

		#region Helpers

		private static void ThrowIfError(string responseString)
		{
			if (HwiParser.TryParseErrors(responseString, out HwiException error))
			{
				throw error;
			}
		}

		private static HwiOption[] BuildOptions(HardwareWalletVendors? deviceType, string devicePath, HDFingerprint? fingerprint, params HwiOption[] extraOptions)
		{
			var options = new List<HwiOption>();

			var hasDevicePath = devicePath != null;
			var hasDeviceType = deviceType.HasValue;
			var hasFingerprint = fingerprint.HasValue;

			// Fingerprint and devicetype-devicepath pair cannot happen the same time.
			var notSupportedExceptionMessage = $"Provide either {nameof(fingerprint)} or {nameof(devicePath)}-{nameof(deviceType)} pair, not both.";
			if (hasDeviceType)
			{
				Guard.NotNull(nameof(devicePath), devicePath);
				if (hasFingerprint)
				{
					throw new NotSupportedException(notSupportedExceptionMessage);
				}
			}
			if (hasFingerprint)
			{
				if (hasDevicePath || hasDeviceType)
				{
					throw new NotSupportedException(notSupportedExceptionMessage);
				}
			}

			if (hasDevicePath)
			{
				options.Add(HwiOption.DevicePath(devicePath));
			}
			if (hasDeviceType)
			{
				options.Add(HwiOption.DeviceType(deviceType.Value));
			}
			if (hasFingerprint)
			{
				options.Add(HwiOption.Fingerprint(fingerprint.Value));
			}
			foreach (var opt in extraOptions)
			{
				options.Add(opt);
			}

			return options.ToArray();
		}

		#endregion Helpers
	}
}
