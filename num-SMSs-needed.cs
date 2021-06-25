/*----------------------------------------------------------------------------*\
  LIMITED TO .NET 4.7.2 DUE TO BUILD CHAIN LIMITATIONS
  - Would use local functions, but limited to delegates instead.
\*----------------------------------------------------------------------------*/

using System;
using System.Linq;
using System.Text;

namespace Program
{
    public class SMSInfo
    {

        // These are all the GSM-7 characters. Including the escape chars.
        // https://en.wikipedia.org/wiki/GSM_03.38
        // Every char here fits into 16-bits (when encoded using UTF-16).
        // Could put directly into a string, but char[] seems clearer.
        //
        // Not used in favour of optimisations (below).
        //
        // static private readonly char[] GSM7 =
        //   {'@', '£', '$', '¥', 'è', 'é', 'ù', 'ì', 'ò', 'Ç', (char)10,
        //    'Ø', 'ø', (char)13, 'Å', 'å', 'Δ', '_', 'Φ', 'Γ', 'Λ', 'Ω', 'Π',
        //    'Ψ', 'Σ', 'Θ', 'Ξ', (char)27, 'Æ', 'æ', 'ß', 'É', (char)32,
        //    '!', '"', '#', '¤', '%', '&', '\'', '(', ')', '*', '+', ',', '-',
        //    '.', '/', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ':',
        //    ';', '<', '=', '>', '?', '¡', 'A', 'B', 'C', 'D', 'E', 'F',
        //    'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S',
        //    'T', 'U', 'V', 'W', 'X', 'Y', 'Z', 'Ä', 'Ö', 'Ñ', 'Ü', '§', '¿',
        //    'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
        //    'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        //    'ä', 'ö', 'ñ', 'ü', 'à', (char)12, '^', '{', '}', '\\', '[', '~',
        //    ']', '|', '€'
        //   };


        const string GSM7_Half2 =
		      "èéùìò\u000AØø\u000DΔ_ΦΓΛΩΠΨΣΘΞ\u001BßÉ¡ÖÑÜ§¿öñüà\u000C^{}\\[~]|€";

        static private bool NotInGSM7_Half2 (char c)
        {
          return ! SMSInfo.GSM7_Half2.Contains(c);
        }

        static private bool IsHighSurrogate (char c)
        {
          // docs.microsoft.com/en-us/dotnet/api/system.char.ishighsurrogate
          return c >= 0XD800 && c <= 0XDBFF;
        }

        static private bool IsInGSM7_Half1 (char c)
        {
            return (c >= 0x61 && c <= 0x7A)       // a-z
                   || (c >= 0x20 && c <= 0x5A)    // space ! " # $ % & ' ( ) * +
                                                  // , - . / 0-9 : ; < = > ? @
                                                  // A-Z
                   || (c >= 0xA3 && c <= 0xA5)    // £ ¤ ¥
                   || (c >= 0xC4 && c <= 0xC7)    // Ä Å Æ Ç
                   || (c >= 0xE4 && c <= 0xE6);   // ä å æ
        }

        // Are there any non GSM-7 characters in this string?
        static private bool AreNonGSM7CharsUsed (string s)
        {
          foreach (char c in s)
          {
            if (SMSInfo.IsInGSM7_Half1(c))
              continue;

            if (SMSInfo.IsHighSurrogate(c) || SMSInfo.NotInGSM7_Half2(c))
              return true;
          }

          return false;
        }



        /// <summary>
        /// Returns the number of concatenated SMSs required to send a message.
        /// Takes into account the opt-out link appended to the message.
        ///
        /// Assumptions:
        /// - Maximum message size is 1120 bits (limited by signaling protocol)
        ///   https://en.wikipedia.org/wiki/SMS#Message_size
        ///
        /// - If all characters (opt-out link included) are within the GSM-7
        ///   code sheet, the message is encoded using GSM-7, else UTF-16 used.
        ///   https://en.wikipedia.org/wiki/GSM_03.38
        ///   (ie. 8-bit encoding is never used)
        ///
        /// - GSM-7 regular characters (1 code point) take 7 bits
        ///   GSM-7 escape characters (2 code points) take 2 * 7 bits
        ///   UFT-16 chars able to fit in 16 bits (1 code point), take 16 bits.
        ///   UFT-16 chars not fitting in 16 bits (2 code points), take 32 bits.
        ///
        /// - Concatenated SMSs use a 6 octet (48 bit) User Data Header (UDH).
        ///   (rather than 7 octets - like when using 16-bit CSMS Reference)
        ///   https://en.wikipedia.org/wiki/Concatenated_SMS
        ///
        /// - Thus character limits are:
        ///   GSM-7
        ///    - single message: 160 * 7-bit characters (code points)
        ///    - concatenated msg: 48-bit UDH + 153 * 7-bit characters, per msg
        ///   UTF-16
        ///    - single message: 70 * 16-bit characters (code points)
        ///    - concatenated msg: 48-bit UDH + 67 * 16-bit chars, per msg
        ///
        /// - Character encoding cannot be split over messages.
        ///   For example, if only 7 bits remain free in the current message and
        ///   the next character is a GSM-7 escape character (takes 2 * 7 bits),
        ///   it must be encocded at the beginning of the next message.
        ///   The 7 bits in the current message are wasted (get padded).
        ///   Similarly for 32-bit UTF-16 characters (ie. 2 code points).
        /// </summary>
        ///
        /// <param name="msg"></param>
        /// <param name="ooLinkLen">Number of characters (code points) of
        /// opt-out link when encoded along with message.
        /// Legally required with every marketting SMS.</param>
        /// <param name="numSMSs"></param>

        public void NumSMSsNeeded(string msg, int ooLinkLen, out int numSMSs)
        {
            // Strip CR from all CRLF so it doesn't muck up the character count.
            // SMS providers use linux (ie. \n = LF = 1 char)
            StringBuilder sb = new StringBuilder(1000, 5000);

            foreach (char c in msg)
            {
                if (c != 0x0D) sb.Append(c);
            }

            msg = sb.ToString();


            // number of chars (code points) of current character
            Func<char, int> LenOfGSM7Char =
              c => "\u000C^{}\\[~]|€".Contains(c) ? 2 : 1;


            if (SMSInfo.AreNonGSM7CharsUsed(msg))
            {
                // msg will be encoded using UTF-16

                numSMSs = msg.Length + ooLinkLen <= 70
                          ? 1
                          : ((msg.Length + ooLinkLen) / 67
                             + ((msg.Length + ooLinkLen) % 67 == 0 ? 0 : 1)
                            );
            }
            else
            {
                // msg will be encoded using GSM-7

                numSMSs = 1;
                int cnt = 0;

                for (int i = 0; i < msg.Length; ++i)
                {
                    int w = LenOfGSM7Char(msg[i]);

                    if (numSMSs == 1)
                    {
                        if (cnt + w > 160)
                        {
                            // too many chars. need another message.

                            // reduce char (code point) count to account
                            // for need to add a User Data Header
                            while (cnt > 153)
                            {
                                --i;
                                cnt -= LenOfGSM7Char(msg[i]);
                            }

                            // adjust for increment to occur after this loop
                            --i;

                            // deliberate overcount to use numSMSs > 1 branch
                            ++numSMSs;
                        }
                        else
                        {
                            cnt += w;
                        }
                    }
                    else
                    {
                        if (cnt + w > 153)
                        {
                            // User Data Header already accounted for
                            ++numSMSs;
                            cnt = w;
                        }
                        else
                        {
                            cnt += w;
                        }
                    }
                }

                // adjust for deliberate overcounting above
                numSMSs -= numSMSs > 1 ? 1 : 0;

                // adjust for opt-out link
                int m = numSMSs == 1 ? 160 : 153;

                if (cnt + ooLinkLen > m)
                {
                  // opt-out can be longer than one msg
                  numSMSs += (cnt + ooLinkLen) / m;
                }

            }

        } // NumSMSsNeeded


    } // SMSInfo
} // Program
