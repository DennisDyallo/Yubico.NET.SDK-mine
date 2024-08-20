// Copyright 2021 Yubico AB
//
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Globalization;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using Yubico.Core.Tlv;
using Yubico.YubiKey.Piv.Commands;

namespace Yubico.YubiKey.Piv
{
    // This portion of the PivSession class contains code related to attestation
    // statements.
    public sealed partial class PivSession : IDisposable
    {
        private const int AttestationCertTag = 0x005fff01;
        private const int PivEncodingTag = 0x53;
        private const int PivCertTag = 0x70;
        private const int MaximumCertDerLength = 2800;
        private const int MaximumNameValueLength = 1024;
        private const int MaximumValidityValueLength = 64;

        /// <summary>
        /// Create an attestation statement for the private key in the given slot.
        /// &gt; [!NOTE]
        /// &gt; In version 1.0.0 of the SDK, it was not possible to get an
        /// &gt; attestation statement for keys in slots 82 - 95 (retired key
        /// &gt; slots). However, beginning with SDK 1.0.1, it is possible to get
        /// &gt; attestation statements for keys in those slots.
        /// </summary>
        /// <remarks>
        /// See the User's Manual entry on
        /// <xref href="UsersManualPivAttestation"> PIV attestation </xref> for
        /// more information on attestation statements.
        /// <para>
        /// Note that attestation is a feature available on YubiKeys version 4.3
        /// and later.
        /// </para>
        /// <para>
        /// An attestation statement is an X.509 certificate that certifies a
        /// private key was generated by a YubiKey.
        /// </para>
        /// <para>
        /// It is possible to create attestation statements only for keys
        /// generated on a YubiKey, and only for keys in the following slots:
        /// <code>
        ///   PivSlot.Authentication      = 9A
        ///   PivSlot.Signing             = 9C
        ///   PivSlot.KeyManagement       = 9D
        ///   PivSlot.CardAuthentication  = 9E
        ///   PivSlot.Retired1            = 82
        ///     through
        ///   PivSlot.Retired20           = 95
        /// </code>
        /// If the <c>slotNumber</c> argument is for any other slot, or if there
        /// is no key in the slot, or if the key in the slot was imported and not
        /// generated by the YubiKey, this method will throw an exception.
        /// </para>
        /// <para>
        /// Note that it is not possible to get an attestation statement for the
        /// key in slot F9. That is the attestation key itself.
        /// </para>
        /// <para>
        /// </para>
        /// <para>
        /// The key that will sign the attestation statement is the "attestation
        /// key" in slot F9. To verify the attestation statement, chain up to the
        /// attestation key's cert (see the method <see
        /// cref="GetAttestationCertificate"/>), which will chain to a root.
        /// The YubiKey is manufactured with an attestation key and cert that
        /// chain to the Yubico root cert. The User's Manual entry on
        /// <xref href="UsersManualPivAttestation"> PIV attestation </xref> has
        /// more information on chaining attestation statements.
        /// </para>
        /// <para>
        /// It is possible to replace the attestation key and cert. In that case,
        /// the attestation statement created by this method will chain up to a
        /// different root.
        /// See <see cref="ReplaceAttestationKeyAndCertificate"/>). There are
        /// restrictions on the key and certificate. The documentation for the
        /// Replace method lists those restrictions.
        /// </para>
        /// <para>
        /// It is not necessary to authenticate the management key or verify the
        /// PIN in order to create an attestation statement.
        /// </para>
        /// </remarks>
        /// <param name="slotNumber">
        /// The slot containing the key to be attested.
        /// </param>
        /// <returns>
        /// The resulting attestation statement (a certificate).
        /// </returns>
        /// <exception cref="ArgumentException">
        /// The slot specified is not valid for creating an attestation statement.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The YubiKey is pre-4.3, or there is no YubiKey-generated key in the
        /// slot, or the attestation key and cert were replaced with invalid
        /// values, or the YubiKey could not complete the task for some reason
        /// such as unreliable connection.
        /// </exception>
        public X509Certificate2 CreateAttestationStatement(byte slotNumber)
        {
            if (_yubiKeyDevice.HasFeature(YubiKeyFeature.PivAttestation))
            {
                // This call will throw an exception if the slot number is incorrect.
                var command = new CreateAttestationStatementCommand(slotNumber);
                var response = Connection.SendCommand(command);

                // This call will throw an exception if there was a problem with
                // attestation (imported, invalid cert, etc.).
                return response.GetData();
            }

            throw new NotSupportedException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    ExceptionMessages.NotSupportedByYubiKeyVersion));
        }

        /// <summary>
        /// Get the attestation certificate.
        /// </summary>
        /// <remarks>
        /// Note that attestation is a feature available on YubiKeys version 4.3
        /// and later.
        /// <para>
        /// The private key in slot F9 (<c>PivSlot.Attestation</c>) is the key used
        /// to sign the attestation statement (see
        /// <see cref="CreateAttestationStatement"/>). To verify the
        /// attestation statement, one needs the certificate of the key that signed
        /// it. The certificate returned by this method is that certificate.
        /// <code>
        ///       Root Cert
        ///           |
        ///       [CA Cert] (there may or may not be a CA cert between
        ///           |      the root and Attestation Cert)
        ///           |
        ///      Attestation Cert (returned by this method)
        ///           |
        ///     Attestation Statement (returned by CreateAttestationStatement)
        /// </code>
        /// </para>
        /// <para>
        /// The YubiKey is manufactured with an attestation key and cert that chain
        /// to the Yubico root cert. The User's Manual entry on
        /// <xref href="UsersManualPivAttestation"> PIV attestation </xref> has
        /// more information on chaining attestation statements and certs.
        /// </para>
        /// <para>
        /// It is possible to replace the attestation key and cert. In that case,
        /// the attestation statement created by this method will chain up to a
        /// different root.
        /// See <see cref="ReplaceAttestationKeyAndCertificate"/>).
        /// </para>
        /// </remarks>
        /// <returns>
        /// The attestation cert.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// The YubiKey is pre-4.3, or there is no attestation certificate, or it
        /// could not complete the task for some reason such as unreliable
        /// connection.
        /// </exception>
        /// <exception cref="TlvException">
        /// If the attestation certificate was replaced by data that is not a
        /// certificate.
        /// </exception>
        public X509Certificate2 GetAttestationCertificate()
        {
            if (_yubiKeyDevice.HasFeature(YubiKeyFeature.PivAttestation))
            {
                var command = new GetDataCommand(AttestationCertTag);
                var response = Connection.SendCommand(command);
                var certData = response.GetData();

                var tlvReader = new TlvReader(certData);
                tlvReader = tlvReader.ReadNestedTlv(PivEncodingTag);
                certData = tlvReader.ReadValue(PivCertTag);
                
                return new X509Certificate2(certData.ToArray());
            }

            throw new NotSupportedException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    ExceptionMessages.NotSupportedByYubiKeyVersion));
        }

        /// <summary>
        /// Replace the attestation key and certificate.
        /// </summary>
        /// <remarks>
        /// See the User's Manual entry on
        /// <xref href="UsersManualPivAttestation"> PIV attestation </xref> for
        /// more information on attestation statements and replacing them.
        /// <para>
        /// Note that attestation is a feature available on YubiKeys version 4.3
        /// and later.
        /// </para>
        /// <para>
        /// The YubiKey is manufactured with an attestation key and cert. This is
        /// the key that is used to sign the attestation statement, and the cert
        /// that verifies the attestation key.
        /// </para>
        /// <para>
        /// This key was generated by Yubico, and the cert chains to a Yubico
        /// root. They are shared by many YubiKeys. If you want to replace them
        /// with your own, call this method.
        /// </para>
        /// <para>
        /// Note that if you replace the Yubico key and cert, there is no way to
        /// recover them, they will be gone for good. So use this method with
        /// caution.
        /// </para>
        /// <para>
        /// Note also that this is an operation that very few users will want to
        /// do. There are some companies who want to make sure the cert that is
        /// the attestation statement chains to one of their roots, instead of a
        /// Yubico root. For those companies, this feature is available. However,
        /// this is not common. If you are wondering if you want to replace the
        /// attestation key and cert, you almost certainly do not. If this is not
        /// already a requirement in your application, it almost certainly is not
        /// something you will be doing.
        /// </para>
        /// <para>
        /// There are limitations placed on the key and cert. The key must be
        /// either RSA-2048, RSA-3072, RSA-4096, ECC-P256, or ECC-P384. The cert must be X.509, it
        /// must be version 2 or 3, the full DER encoding of the
        /// <c>SubjectName</c> must be fewer than 1029 bytes, and the total
        /// length of the certificate must be fewer than 3052 bytes. This method
        /// will verify that these restrictions are met.
        /// </para>
        /// <para>
        /// YubiKeys before version 5 allowed 1024-bit RSA keys as the
        /// attestation key. However, this method, regardless of the YubiKey
        /// version, will not accept 1024-bit RSA keys.
        /// </para>
        /// <para>
        /// This method will NOT, however, verify that the public key represented
        /// in the cert is indeed the partner to the private key specified. If
        /// you want to make sure they match, see the  User's Manual entry on
        /// <xref href="UsersManualPivAttestation"> PIV attestation </xref> for a
        /// discussion on verifying compatibility.
        /// </para>
        /// <para>
        /// Due to space and compute limitations, the YubiKey itself does not
        /// verify the inputs before loading them. That means it is possible to
        /// load bad key/cert combinations. For example, it is possible to load a
        /// cert that contains a subject key that is not the partner to the
        /// private key. In that case, the YubiKey will create attestation
        /// statements that do not verify or do not chain to a root. In other
        /// cases, the YubiKey might simply return an error when requested to
        /// build an attestation statement. Hence, you must be certain the key
        /// and cert you load are correct, and you should thoroughly test the
        /// attestation statements before deployment.
        /// </para>
        /// <para>
        /// The method will not verify the cert itself, nor will it verify the
        /// validity dates inside the cert.
        /// </para>
        /// <para>
        /// In order to perform this operation, the management key must be
        /// authenticated during this session. If it has not been authenticated,
        /// this method will call <see cref="AuthenticateManagementKey"/>. That
        /// is, your application does not need to authenticate the management key
        /// separately (i.e., call <c>TryAuthenticateManagementKey</c> or
        /// <c>AuthenticateManagementKey</c>), this method will determine if the
        /// management key has been authenticated or not, and if not, it will
        /// make the call to perform mutual authentication.
        /// </para>
        /// <para>
        /// The authentication method will collect the management key using the
        /// <c>KeyCollector</c> delegate. If no such delegate has been set, it
        /// will throw an exception.
        /// </para>
        /// <para>
        /// The <c>KeyCollector</c> has an option to cancel the operation. That
        /// is, the <c>AuthenticateManagementKey</c> method will call the
        /// <c>KeyCollector</c> requesting the management key, and it is possible
        /// that during the collection operations, the user cancels. The
        /// <c>KeyCollector</c> will return to the authentication method noting
        /// the cancellation. In that case, it will throw an exception. If you
        /// want the authentication to return <c>false</c> on user cancellation,
        /// you must call <see cref="TryAuthenticateManagementKey(bool)"/> directly
        /// before calling this method.
        /// </para>
        /// </remarks>
        /// <param name="privateKey">
        /// The private key that will be the new attestation key.
        /// </param>
        /// <param name="certificate">
        /// The cert that will be the new attestation cert.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// One of the inputs is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// One of the inputs is not allowed (e.g. private key is 1024-bit RSA).
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// There is no <c>KeyCollector</c> loaded, the key provided was not a
        /// valid Triple-DES key, or the YubiKey had some other error, such as
        /// unreliable connection.
        /// </exception>
        /// <exception cref="MalformedYubiKeyResponseException">
        /// The YubiKey returned malformed data and authentication, either single
        /// or double, could not be performed.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// The user canceled management key collection.
        /// </exception>
        /// <exception cref="SecurityException">
        /// Mutual authentication was performed and the YubiKey was not
        /// authenticated.
        /// </exception>
        public void ReplaceAttestationKeyAndCertificate(PivPrivateKey privateKey, X509Certificate2 certificate)
        {
            byte[] certDer = CheckVersionKeyAndCertRequirements(privateKey, certificate);

            var tlvWriter = new TlvWriter();
            using (tlvWriter.WriteNestedTlv(0x53))
            {
                tlvWriter.WriteValue(0x70, certDer);
                tlvWriter.WriteByte(0x71, 0);
                tlvWriter.WriteValue(0xfe, null);
            }
            byte[] encodedCert = tlvWriter.Encode();

            ImportPrivateKey(PivSlot.Attestation, privateKey);

            var command = new PutDataCommand(AttestationCertTag, encodedCert);
            var response = Connection.SendCommand(command);
            if (response.Status != ResponseStatus.Success)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        ExceptionMessages.CommandResponseApduUnexpectedResult,
                        response.StatusWord.ToString("X4", CultureInfo.InvariantCulture)));
            }
        }

        // Make sure the YubiKey version, the certificate, and the key fulfill
        // the requirements.
        // The version must be 4.3 or later.
        // The cert must be X.509, the lengths must be correct, the version must
        // be supported. The algorithm is limited.
        // This will throw an exception if a check fails, or if one or both
        // arguments are null, or the algorithm is unsupported.
        // Return the DER encoding of the certificate.
        private byte[] CheckVersionKeyAndCertRequirements(PivPrivateKey privateKey, X509Certificate2 certificate)
        {
            if (!_yubiKeyDevice.HasFeature(YubiKeyFeature.PivAttestation))
            {
                throw new NotSupportedException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        ExceptionMessages.NotSupportedByYubiKeyVersion));
            }

            if (privateKey is null)
            {
                throw new ArgumentNullException(nameof(privateKey));
            }

            int keySize = 256;
            string algorithm = "ECC";
            switch (privateKey.Algorithm)
            {
                case PivAlgorithm.Rsa2048:
                    keySize = 2048;
                    algorithm = "RSA";
                    break;

                case PivAlgorithm.Rsa3072:
                    keySize = 3072;
                    algorithm = "RSA";
                    break;

                case PivAlgorithm.Rsa4096:
                    keySize = 4096;
                    algorithm = "RSA";
                    break;

                case PivAlgorithm.EccP256:
                    break;

                case PivAlgorithm.EccP384:
                    keySize = 384;
                    break;

                default:
                    throw new ArgumentException(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            ExceptionMessages.UnsupportedAlgorithm));
            }

            bool isValidCert = IsCert(certificate, out byte[] certDer);
            isValidCert = IsCertSameAlgorithm(isValidCert, certificate, keySize, algorithm);
            isValidCert = IsCertNameAndValidity(isValidCert, certDer);

            if (isValidCert == false)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        ExceptionMessages.UnsupportedAttestationCert));
            }

            return certDer;
        }

        // Is there really a cert in this variable? Is it > version 1?
        // If so, set certDer to the DER encoding of the cert.
        private static bool IsCert(X509Certificate2 certificate, out byte[] certDer)
        {
            certDer = Array.Empty<byte>();

            if (certificate is null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            if (certificate.Handle != IntPtr.Zero)
            {
                if (certificate.Version > 1)
                {
                    certDer = certificate.GetRawCertData();
                }
            }

            return certDer.Length > 0 && certDer.Length < MaximumCertDerLength;
        }

        // Does the cert in the object share the algorithm and key size?
        // If the input arg isValidCert is false, don't check, just return false;
        private static bool IsCertSameAlgorithm(bool isValidCert, X509Certificate2 certificate, int keySize, string algorithm)
        {
            bool returnValue = false;

            if (isValidCert)
            {
                // For ECC, the Certificate class's PublicKey property does not
                // have a Key. But the public key is in the EncodedKeyValue
                // property. An encoded ECC public key is a point:
                //   04 || x-coord || y-coord   fixed length
                // The length of each coordinate is the key size. Because keySize
                // is given in bits, and the Length of the encoded key is given
                // in bytes, compare ( 2 * (keySize / 8)) + 1.
                // That's why the comparison is to (keySize / 4) + 1.
                if (certificate.PublicKey.Oid.FriendlyName == algorithm)
                {
                    returnValue = keySize switch
                    {
                        256 => certificate.PublicKey.EncodedKeyValue.RawData.Length == (keySize / 4) + 1,
                        384 => certificate.PublicKey.EncodedKeyValue.RawData.Length == (keySize / 4) + 1,
                        2048 => certificate.PublicKey.Key.KeySize == keySize,
                        _ => false,
                    };
                }
            }

            return returnValue;
        }

        // Does the cert in the object have Validity and IssuerName encodings
        // that are not too long?
        // If the input arg isValidCert is false, don't check, just return false;
        private static bool IsCertNameAndValidity(bool isValidCert, byte[] certDer)
        {
            bool returnValue = false;

            if (isValidCert)
            {
                // Get some of the elements of a cert. We just want to verify
                // their lengths.
                // The cert is the DER encoding of
                //   SEQ {
                //     SEQ {
                //       [0] EXPLICIT INTEGER OPTIONAL,
                //       INTEGER,
                //       AlgId (a SEQ)
                //       IssuerName (a SEQ)
                //       Validity (a SEQ)
                //       SubjectName (a SEQ)
                // We just want to know how long the SubjectName and Validity are,
                // so "decode" them as full elements (don't decode the contents
                // of the IssuerName and Validity SEQUENCEs).
                var reader = new TlvReader(certDer);
                if (reader.TryReadNestedTlv(out reader, 0x30))
                {
                    if (reader.TryReadNestedTlv(out reader, 0x30))
                    {
                        byte[] tags = new byte[] { 0xA0, 0x02, 0x30, 0x30, 0x30, 0x30 };
                        var value = new ReadOnlyMemory<byte>[tags.Length];
                        int index = 0;
                        for (; index < tags.Length; index++)
                        {
                            if (reader.TryReadValue(out value[index], tags[index]) == false)
                            {
                                break;
                            }
                        }

                        if (index >= tags.Length)
                        {
                            returnValue = value[4].Length < MaximumValidityValueLength &&
                                          value[5].Length < MaximumNameValueLength;
                        }
                    }
                }
            }

            return returnValue;
        }
    }
}
