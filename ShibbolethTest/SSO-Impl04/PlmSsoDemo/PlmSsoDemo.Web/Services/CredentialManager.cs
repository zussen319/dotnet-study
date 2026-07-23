using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

// =============================================================================
//  Windows 資格情報マネージャーからの読み取り
//
//  ★目的
//    JWT の署名鍵を Web.config に平文で持たせず、
//    Windows の資格情報ストア（Credential Manager）から取得する。
//
//  ★重要な性質
//    資格情報は「利用者ごと」に保管され、その利用者の DPAPI で暗号化される。
//    したがって、
//      ・IIS のアプリケーションプールを専用アカウントで動かす
//      ・そのアカウントで資格情報を登録する
//      ・アプリケーションプールの「ユーザープロファイルの読み込み」を有効にする
//    の3つが揃って初めて読み取れる。（詳細は README_phase4.md）
//
//  ★.NET には資格情報マネージャーの API が無いため、
//    Win32 API（advapi32.dll の CredRead）を P/Invoke で呼び出す。
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.Web.Services
{
    public static class CredentialManager
    {
        private const int CRED_TYPE_GENERIC = 1;
        private const int ERROR_NOT_FOUND = 1168;

        [DllImport("advapi32.dll", EntryPoint = "CredReadW",
                   CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int reservedFlag,
                                            out IntPtr credentialPtr);

        [DllImport("advapi32.dll", EntryPoint = "CredFree")]
        private static extern void CredFree(IntPtr credentialPtr);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        /// <summary>
        /// 汎用資格情報（Generic Credential）から値を読み取る。
        /// 見つからない場合や読み取れない場合は null を返し、reason に理由を入れる。
        /// </summary>
        /// <param name="target">資格情報の名前（例: PlmSsoDemo/JwtSigningKey）</param>
        public static string Read(string target, out string reason)
        {
            if (string.IsNullOrEmpty(target))
            {
                reason = "資格情報の名前が指定されていません";
                return null;
            }

            IntPtr ptr = IntPtr.Zero;
            try
            {
                if (!CredRead(target, CRED_TYPE_GENERIC, 0, out ptr))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == ERROR_NOT_FOUND)
                    {
                        reason = "資格情報 '" + target + "' が見つかりません。" +
                                 "アプリケーションプールの ID で登録されているか、" +
                                 "「ユーザープロファイルの読み込み」が有効か確認してください。";
                    }
                    else
                    {
                        reason = "資格情報の読み取りに失敗しました（Win32 エラー " + err + ": " +
                                 new Win32Exception(err).Message + "）";
                    }
                    return null;
                }

                CREDENTIAL cred = (CREDENTIAL)Marshal.PtrToStructure(ptr, typeof(CREDENTIAL));

                if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero)
                {
                    reason = "資格情報 '" + target + "' の値が空です";
                    return null;
                }

                // cmdkey / 資格情報マネージャーは値を UTF-16（Unicode）で保管する。
                // CredentialBlobSize はバイト数なので、文字数は 2 で割った値。
                string value = Marshal.PtrToStringUni(cred.CredentialBlob,
                                                      cred.CredentialBlobSize / 2);

                reason = null;
                return value;
            }
            catch (Exception ex)
            {
                reason = "資格情報の読み取り中に例外が発生しました: " + ex.Message;
                return null;
            }
            finally
            {
                if (ptr != IntPtr.Zero) CredFree(ptr);
            }
        }

        /// <summary>
        /// 現在アプリケーションを実行している Windows アカウント名（診断用）。
        /// どの利用者の資格情報ストアを見に行くのかを確認するために使う。
        /// </summary>
        public static string CurrentIdentityName
        {
            get
            {
                try
                {
                    return System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                }
                catch (Exception ex)
                {
                    return "(取得できません: " + ex.Message + ")";
                }
            }
        }
    }
}
