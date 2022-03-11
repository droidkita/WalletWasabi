using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json.Linq;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Wallets;

namespace WalletWasabi.Helpers;

public static class ImportWalletHelper
{
	private const string WalletExistsErrorMessage = "Wallet with the same fingerprint already exists!";

	public static async Task<KeyManager> ImportWalletAsync(WalletManager walletManager, string walletName, string filePath)
	{
		var walletFullPath = walletManager.WalletDirectories.GetWalletFilePaths(walletName).walletFilePath;

		string jsonString = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
		var jsonWallet = JObject.Parse(jsonString);

		// Older CC wallet file does not contain 'ColdCardFirmwareVersion' but contains the same keys as Wasabi, so we are checking the number
		var isColdcardJson = jsonWallet.Count <= 3;

		KeyManager km = isColdcardJson
			? GetKeyManagerByColdcardJson(walletManager, jsonWallet, walletFullPath)
			: GetKeyManagerByWasabiJson(walletManager, filePath, walletFullPath);

		if (isColdcardJson)
		{
			km.SetIcon(WalletType.Coldcard);
		}

		km.SetBestHeight(0);

		return km;
	}

	private static KeyManager GetKeyManagerByWasabiJson(WalletManager manager, string filePath, string walletFullPath)
	{
		var km = KeyManager.FromFile(filePath);

		if (manager.WalletExists(km.MasterFingerprint))
		{
			throw new InvalidOperationException(WalletExistsErrorMessage);
		}

		km.SetFilePath(walletFullPath);

		return km;
	}

	private static KeyManager GetKeyManagerByColdcardJson(WalletManager manager, JObject jsonWallet, string walletFullPath)
	{
		var xpubString = jsonWallet["ExtPubKey"]?.ToString();
		if (xpubString is null)
		{
			throw new ArgumentNullException($"Can't get KeyManager, ExtPubKey was null.");
		}

		var mfpString = jsonWallet["MasterFingerprint"]?.ToString();
		if (mfpString is null)
		{
			throw new ArgumentNullException($"Can't get KeyManager, MasterFingerprint was null.");
		}
		// https://github.com/zkSNACKs/WalletWasabi/pull/1663#issuecomment-508073066
		// Coldcard 2.1.0 improperly implemented Wasabi skeleton fingerprint at first, so we must reverse byte order.
		// The solution was to add a ColdCardFirmwareVersion json field from 2.1.1 and correct the one generated by 2.1.0.
		var coldCardVersionString = jsonWallet["ColdCardFirmwareVersion"]?.ToString();
		var reverseByteOrder = false;
		if (coldCardVersionString is null)
		{
			reverseByteOrder = true;
		}
		else
		{
			Version coldCardVersion = new(coldCardVersionString);

			if (coldCardVersion == new Version("2.1.0")) // Should never happen though.
			{
				reverseByteOrder = true;
			}
		}

		var bytes = ByteHelpers.FromHex(Guard.NotNullOrEmptyOrWhitespace(nameof(mfpString), mfpString, trim: true));
		HDFingerprint mfp = reverseByteOrder ? new HDFingerprint(bytes.Reverse().ToArray()) : new HDFingerprint(bytes);

		if (manager.WalletExists(mfp))
		{
			throw new InvalidOperationException(WalletExistsErrorMessage);
		}

		ExtPubKey extPubKey = NBitcoinHelpers.BetterParseExtPubKey(xpubString);

		return KeyManager.CreateNewHardwareWalletWatchOnly(mfp, extPubKey, manager.Network, walletFullPath);
	}
}
