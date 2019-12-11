// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace WixToolset.BootstrapperCore
{
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using System.Security;
    using System.Text;

    /// <summary>
    /// Container class for the <see cref="IBootstrapperEngine"/> interface.
    /// </summary>
    public sealed class Engine : IEngine
    {
        // Burn errs on empty strings, so declare initial buffer size.
        private const int InitialBufferSize = 80;
        private static readonly string normalizeVersionFormatString = "{0} must be less than or equal to " + UInt16.MaxValue;

        private IBootstrapperEngine engine;
        private Variables<long> numericVariables;
        private Variables<SecureString> secureStringVariables;
        private Variables<string> stringVariables;
        private Variables<Version> versionVariables;

        /// <summary>
        /// Creates a new instance of the <see cref="Engine"/> container class.
        /// </summary>
        /// <param name="engine">The <see cref="IBootstrapperEngine"/> to contain.</param>
        internal Engine(IBootstrapperEngine engine)
        {
            this.engine = engine;

            // Wrap the calls to get and set numeric variables.
            this.numericVariables = new Variables<long>(
                delegate(string name)
                {
                    long value;
                    int ret = this.engine.GetVariableNumeric(name, out value);
                    if (NativeMethods.S_OK != ret)
                    {
                        throw new Win32Exception(ret);
                    }

                    return value;
                },
                delegate(string name, long value)
                {
                    this.engine.SetVariableNumeric(name, value);
                },
                delegate(string name)
                {
                    long value;
                    int ret = this.engine.GetVariableNumeric(name, out value);

                    return NativeMethods.E_NOTFOUND != ret;
                }
            );

            // Wrap the calls to get and set string variables using SecureStrings.
            this.secureStringVariables = new Variables<SecureString>(
                delegate(string name)
                {
                    var pUniString = this.getStringVariable(name, out var length);
                    try
                    {
                        return this.convertToSecureString(pUniString, length);
                    }
                    finally
                    {
                        if (IntPtr.Zero != pUniString)
                        {
                            Marshal.FreeCoTaskMem(pUniString);
                        }
                    }
                },
                delegate(string name, SecureString value)
                {
                    IntPtr pValue = Marshal.SecureStringToCoTaskMemUnicode(value);
                    try
                    {
                        this.engine.SetVariableString(name, pValue);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pValue);
                    }
                },
                delegate(string name)
                {
                    return this.containsVariable(name);
                }
            );

            // Wrap the calls to get and set string variables.
            this.stringVariables = new Variables<string>(
                delegate(string name)
                {
                    int length;
                    IntPtr pUniString = this.getStringVariable(name, out length);
                    try
                    {
                        return Marshal.PtrToStringUni(pUniString, length);
                    }
                    finally
                    {
                        if (IntPtr.Zero != pUniString)
                        {
                            Marshal.FreeCoTaskMem(pUniString);
                        }
                    }
                },
                delegate(string name, string value)
                {
                    IntPtr pValue = Marshal.StringToCoTaskMemUni(value);
                    try
                    {
                        this.engine.SetVariableString(name, pValue);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pValue);
                    }
                },
                delegate(string name)
                {
                    return this.containsVariable(name);
                }
            );

            // Wrap the calls to get and set version variables.
            this.versionVariables = new Variables<Version>(
                delegate(string name)
                {
                    long value;
                    int ret = this.engine.GetVariableVersion(name, out value);
                    if (NativeMethods.S_OK != ret)
                    {
                        throw new Win32Exception(ret);
                    }

                    return LongToVersion(value);
                },
                delegate(string name, Version value)
                {
                    long version = VersionToLong(value);
                    this.engine.SetVariableVersion(name, version);
                },
                delegate(string name)
                {
                    long value;
                    int ret = this.engine.GetVariableVersion(name, out value);

                    return NativeMethods.E_NOTFOUND != ret;
                }
            );
        }

        public IVariables<long> NumericVariables
        {
            get { return this.numericVariables; }
        }

        public int PackageCount
        {
            get
            {
                int count;
                this.engine.GetPackageCount(out count);

                return count;
            }
        }

        public IVariables<SecureString> SecureStringVariables
        {
            get { return this.secureStringVariables; }
        }

        public IVariables<string> StringVariables
        {
            get { return this.stringVariables; }
        }

        public IVariables<Version> VersionVariables
        {
            get { return this.versionVariables; }
        }

        public void Apply(IntPtr hwndParent)
        {
            this.engine.Apply(hwndParent);
        }

        public void CloseSplashScreen()
        {
            this.engine.CloseSplashScreen();
        }

        public void Detect()
        {
            this.Detect(IntPtr.Zero);
        }

        public void Detect(IntPtr hwndParent)
        {
            this.engine.Detect(hwndParent);
        }

        public bool Elevate(IntPtr hwndParent)
        {
            int ret = this.engine.Elevate(hwndParent);

            if (NativeMethods.S_OK == ret || NativeMethods.E_ALREADYINITIALIZED == ret)
            {
                return true;
            }
            else if (NativeMethods.E_CANCELLED == ret)
            {
                return false;
            }
            else
            {
                throw new Win32Exception(ret);
            }
        }

        public string EscapeString(string input)
        {
            int capacity = InitialBufferSize;
            StringBuilder sb = new StringBuilder(capacity);

            // Get the size of the buffer.
            int ret = this.engine.EscapeString(input, sb, ref capacity);
            if (NativeMethods.E_INSUFFICIENT_BUFFER == ret || NativeMethods.E_MOREDATA == ret)
            {
                sb.Capacity = ++capacity; // Add one for the null terminator.
                ret = this.engine.EscapeString(input, sb, ref capacity);
            }

            if (NativeMethods.S_OK != ret)
            {
                throw new Win32Exception(ret);
            }

            return sb.ToString();
        }

        public bool EvaluateCondition(string condition)
        {
            bool value;
            this.engine.EvaluateCondition(condition, out value);

            return value;
        }

        public string FormatString(string format)
        {
            int capacity = InitialBufferSize;
            StringBuilder sb = new StringBuilder(capacity);

            // Get the size of the buffer.
            int ret = this.engine.FormatString(format, sb, ref capacity);
            if (NativeMethods.E_INSUFFICIENT_BUFFER == ret || NativeMethods.E_MOREDATA == ret)
            {
                sb.Capacity = ++capacity; // Add one for the null terminator.
                ret = this.engine.FormatString(format, sb, ref capacity);
            }

            if (NativeMethods.S_OK != ret)
            {
                throw new Win32Exception(ret);
            }

            return sb.ToString();
        }

        public void LaunchApprovedExe(IntPtr hwndParent, string approvedExeForElevationId, string arguments)
        {
            this.LaunchApprovedExe(hwndParent, approvedExeForElevationId, arguments, 0);
        }

        public void LaunchApprovedExe(IntPtr hwndParent, string approvedExeForElevationId, string arguments, int waitForInputIdleTimeout)
        {
            this.engine.LaunchApprovedExe(hwndParent, approvedExeForElevationId, arguments, waitForInputIdleTimeout);
        }

        public void Log(LogLevel level, string message)
        {
            this.engine.Log(level, message);
        }

        public void Plan(LaunchAction action)
        {
            this.engine.Plan(action);
        }

        public void SetUpdate(string localSource, string downloadSource, long size, UpdateHashType hashType, byte[] hash)
        {
            this.engine.SetUpdate(localSource, downloadSource, size, hashType, hash, null == hash ? 0 : hash.Length);
        }

        public void SetLocalSource(string packageOrContainerId, string payloadId, string path)
        {
            this.engine.SetLocalSource(packageOrContainerId, payloadId, path);
        }

        public void SetDownloadSource(string packageOrContainerId, string payloadId, string url, string user, string password)
        {
            this.engine.SetDownloadSource(packageOrContainerId, payloadId, url, user, password);
        }

        public int SendEmbeddedError(int errorCode, string message, int uiHint)
        {
            int result = 0;
            this.engine.SendEmbeddedError(errorCode, message, uiHint, out result);
            return result;
        }

        public int SendEmbeddedProgress(int progressPercentage, int overallPercentage)
        {
            int result = 0;
            this.engine.SendEmbeddedProgress(progressPercentage, overallPercentage, out result);
            return result;
        }

        public void Quit(int exitCode)
        {
            this.engine.Quit(exitCode);
        }

        internal sealed class Variables<T> : IVariables<T>
        {
            // .NET 2.0 does not support Func<T, TResult> or Action<T1, T2>.
            internal delegate T Getter<T>(string name);
            internal delegate void Setter<T>(string name, T value);

            private Getter<T> getter;
            private Setter<T> setter;
            private Predicate<string> contains;

            internal Variables(Getter<T> getter, Setter<T> setter, Predicate<string> contains)
            {
                this.getter = getter;
                this.setter = setter;
                this.contains = contains;
            }

            public T this[string name]
            {
                get { return this.getter(name); }
                set { this.setter(name, value); }
            }

            public bool Contains(string name)
            {
                return this.contains(name);
            }
        }

        /// <summary>
        /// Gets whether the variable given by <paramref name="name"/> exists.
        /// </summary>
        /// <param name="name">The name of the variable to check.</param>
        /// <returns>True if the variable given by <paramref name="name"/> exists; otherwise, false.</returns>
        internal bool containsVariable(string name)
        {
            int capacity = 0;
            IntPtr pValue = IntPtr.Zero;
            int ret = this.engine.GetVariableString(name, pValue, ref capacity);

            return NativeMethods.E_NOTFOUND != ret;
        }

        /// <summary>
        /// Gets the variable given by <paramref name="name"/> as a string.
        /// </summary>
        /// <param name="name">The name of the variable to get.</param>
        /// <param name="length">The length of the Unicode string.</param>
        /// <returns>The value by a pointer to a Unicode string.  Must be freed by Marshal.FreeCoTaskMem.</returns>
        /// <exception cref="Exception">An error occurred getting the variable.</exception>
        internal IntPtr getStringVariable(string name, out int length)
        {
            int capacity = InitialBufferSize;
            bool success = false;
            IntPtr pValue = Marshal.AllocCoTaskMem(capacity * UnicodeEncoding.CharSize);
            try
            {
                // Get the size of the buffer.
                int ret = this.engine.GetVariableString(name, pValue, ref capacity);
                if (NativeMethods.E_INSUFFICIENT_BUFFER == ret || NativeMethods.E_MOREDATA == ret)
                {
                    // Don't need to add 1 for the null terminator, the engine already includes that.
                    pValue = Marshal.ReAllocCoTaskMem(pValue, capacity * UnicodeEncoding.CharSize);
                    ret = this.engine.GetVariableString(name, pValue, ref capacity);
                }

                if (NativeMethods.S_OK != ret)
                {
                    throw Marshal.GetExceptionForHR(ret);
                }

                // The engine only returns the exact length of the string if the buffer was too small, so calculate it ourselves.
                for (length = 0; length < capacity; ++length)
                {
                    if(0 == Marshal.ReadInt16(pValue, length * UnicodeEncoding.CharSize))
                    {
                        break;
                    }
                }

                success = true;
                return pValue;
            }
            finally
            {
                if (!success && IntPtr.Zero != pValue)
                {
                    Marshal.FreeCoTaskMem(pValue);
                }
            }
        }

        /// <summary>
        /// Initialize a SecureString with the given Unicode string.
        /// </summary>
        /// <param name="pUniString">Pointer to Unicode string.</param>
        /// <param name="length">The string's length.</param>
        internal SecureString convertToSecureString(IntPtr pUniString, int length)
        {
            if (IntPtr.Zero == pUniString)
            {
                return null;
            }

            SecureString value = new SecureString();
            short s;
            char c;
            for (int charIndex = 0; charIndex < length; charIndex++)
            {
                s = Marshal.ReadInt16(pUniString, charIndex * UnicodeEncoding.CharSize);
                c = (char)s;
                value.AppendChar(c);
                s = 0;
                c = (char)0;
            }
            return value;
        }

        public static long VersionToLong(Version version)
        {
            // In Windows, each version component has a max value of 65535,
            // so we truncate the version before shifting it, which will overflow if invalid.
            long major = (long)(ushort)version.Major << 48;
            long minor = (long)(ushort)version.Minor << 32;
            long build = (long)(ushort)version.Build << 16;
            long revision = (long)(ushort)version.Revision;

            return major | minor | build | revision;
        }

        public static Version LongToVersion(long version)
        {
            int major = (int)((version & ((long)0xffff << 48)) >> 48);
            int minor = (int)((version & ((long)0xffff << 32)) >> 32);
            int build = (int)((version & ((long)0xffff << 16)) >> 16);
            int revision = (int)(version & 0xffff);

            return new Version(major, minor, build, revision);
        }

        /// <summary>
        /// Verifies that VersionVariables can pass on the given Version to the engine.
        /// If the Build or Revision fields are undefined, they are set to zero.
        /// </summary>
        public static Version NormalizeVersion(Version version)
        {
            if (version == null)
            {
                throw new ArgumentNullException("version");
            }

            int major = version.Major;
            int minor = version.Minor;
            int build = version.Build;
            int revision = version.Revision;

            if (major > UInt16.MaxValue)
            {
                throw new ArgumentOutOfRangeException("version", String.Format(normalizeVersionFormatString, "Major"));
            }
            if (minor > UInt16.MaxValue)
            {
                throw new ArgumentOutOfRangeException("version", String.Format(normalizeVersionFormatString, "Minor"));
            }
            if (build > UInt16.MaxValue)
            {
                throw new ArgumentOutOfRangeException("version", String.Format(normalizeVersionFormatString, "Build"));
            }
            if (build == -1)
            {
                build = 0;
            }
            if (revision > UInt16.MaxValue)
            {
                throw new ArgumentOutOfRangeException("version", String.Format(normalizeVersionFormatString, "Revision"));
            }
            if (revision == -1)
            {
                revision = 0;
            }

            return new Version(major, minor, build, revision);
        }
    }
}
