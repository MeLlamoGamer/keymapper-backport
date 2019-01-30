using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using KeyMapper.Classes.Interop;

namespace KeyMapper.Classes
{
	
	public delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    internal class KeySniffer : IDisposable
	{
	    private const int WH_KEYBOARD_LL = 13;
		private const int WM_KEYDOWN = 0x100;
		private const int WM_SYSKEYDOWN = 0x104;

		internal LowLevelKeyboardProc _proc;

		// Implemented a subclass of CriticalHandleZeroOrMinusOneIsInvalid
		// to make sure handle is released, but it meant giving up too much control
		// of when the hook is deactivated.
		
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2006:UseSafeHandleToEncapsulateNativeResources")]
		private IntPtr _hookID;

		private bool _suppress;
		private bool disposed = false;

		public event EventHandler<KeyMapperKeyPressedEventArgs> KeyPressed;

	    public KeySniffer(bool suppress)
		{
            _suppress = suppress;
		}

		// Default to not suppressing keypresses
		public KeySniffer()
			: this(false)
		{ }

		~KeySniffer()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					if (_hookID != IntPtr.Zero)
					{
						Unhook();
					}
				}
                disposed = true;
			}
		}


		public void ActivateHook()
		{
			if (_hookID != IntPtr.Zero)
			{
				// Already hooked..
				return;
			}

			if (_proc == null)
			{
                _proc = HookCallback;
				GC.KeepAlive(_proc);
			}

			Hook();

		}

		public void DeactivateHook()
		{

			if (_hookID == IntPtr.Zero)
			{
				//  there is no hook..
				return;
			}

			Unhook();
		}

	    [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
		private void Hook()
		{

			using (var curProcess = Process.GetCurrentProcess())
			using (var curModule = curProcess.MainModule)
			{
                _hookID = NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
													NativeMethods.GetModuleHandle(curModule.ModuleName), 0);

				if (_hookID == IntPtr.Zero)
				{
					int errorCode = Marshal.GetLastWin32Error();
					throw new System.ComponentModel.Win32Exception(errorCode);
				}
			}
		}

		[SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
		private void Unhook()
		{

			// Documentation on UnhookWindows Ex states:
			// "If the function succeeds, the return value is nonzero.
			// If the function fails, the return value is zero. To get extended error information, call GetLastError."

			// This generates FXCop warning "Method called GetLastWin32Error but the immediately 
			// preceding call to IntPtr.op_Explicit(IntPtr):Int32 is not a platform invoke statement. Move the call to 
			// GetLastWin32Error so that it immediately follows the relevant platform invoke call."

			// Sure looks to me like that's wrong, probable because the method is in a different class, which
			// fxcop told me to do in the first place. 

			if (_hookID == IntPtr.Zero) {
				return;
			}

			int result = (int)NativeMethods.UnhookWindowsHookEx(_hookID);
			int error = Marshal.GetLastWin32Error();

			if (result == 0)
			{
				if (error != 1404) // 1404 is 'Invalid hook handle.'
				{
					// Well, this is bad. A key-suppressing keyboard hook that fails to unhook could paralyse the system.
					// Throwing a hissy fit isn't going to achieve anything though.
					Console.WriteLine("UnhookWindowsEx failed with error code {0}", error);
				}
			}

            _hookID = IntPtr.Zero;
            _proc = null;

		}

		private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
		{
			if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
			{
				// Cast lParam into our structure
				var keypress = (KBHookStruct)Marshal.PtrToStructure(lParam, typeof(KBHookStruct));
                
				//  Console.WriteLine("ScanCode: {0}, Extended: {1}, KeyCode: {2}, Name: {3}",
				 //  keypress.ScanCode, keypress.Extended, keypress.VirtualKeyCode, AppController.GetKeyName(keypress.ScanCode, keypress.Extended));

				if (keypress.ScanCode == 541)
				{
					// Right Alt, at least on my Dell SK-8115 keyboard
					// Console.WriteLine("Fixing Dell's Right Alt keyboard bug");

					keypress.ScanCode = 56;
					keypress.KeyFlags = 1;

				}

				if (keypress.VirtualKeyCode == 19)
				{
					// Pause. This doesn't capture well - it's extended value is 225
					// rather than 224, so 
	
					keypress.ScanCode = 29;
					keypress.KeyFlags = 2;

				}

                // Some keyboards report Num Lock as having the extended bit set
                // on keypress, but that doesn't work in a mapping.
                if (keypress.ScanCode == 69 && keypress.Extended == 224)
                {
                    // The Keyboard lies.
                    keypress.Extended = 0;
                }

				// Raise the event:
				if (KeyPressed != null)
				{
					var e = new KeyMapperKeyPressedEventArgs(keypress);
                    KeyPressed(new object(), e);
				}

				if (_suppress)
				{
					// Return 1 to suppress the keypress.
					return (IntPtr)1;
				}
			}
			return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
		}
	}

    [StructLayout(LayoutKind.Sequential)]
	public struct KBHookStruct
	{
		private int _vkcode;
		private int _scanCode;
		private int _flags;
		private int _time;
		private int _extrainfo;

		private const int LLKHF_EXTENDED = 0x1;
		private const int LLKHF_EXTENDED_PAUSE = 0x2;


		public int VirtualKeyCode
		{
			get { return _vkcode; }
		}

		public int ScanCode
		{
			get { return _scanCode; }
			set { _scanCode = value; }
		}

		public int Extended
		{
			get
			{

				if ((LLKHF_EXTENDED & _flags) == LLKHF_EXTENDED)
				{
					return 224;
				}
				else if ((LLKHF_EXTENDED_PAUSE & _flags) == LLKHF_EXTENDED_PAUSE)
				{
					return 225;
				}
				else
				{
					return 0;
				}
			}
            set
            {
                if (value == 224) {
					_flags = LLKHF_EXTENDED;
				}
				else {
					_flags = 0;
				}
			}
		}

		// They *are* flags.
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1726:UsePreferredTerms", MessageId = "Flags")]
		public int KeyFlags
		{
			get { return _flags; }
			set { _flags = value; }
		}

		public static bool operator ==(KBHookStruct key1, KBHookStruct key2)
		{
			// If ScanCode and Extended are the same, it's the same key.
			return (key1.ScanCode == key2.ScanCode && key1.Extended == key2.Extended);
		}

		public override bool Equals(object obj)
		{
			return (obj is KBHookStruct @struct && this == @struct);
		}

		// override object.GetHashCode
		public override int GetHashCode()
		{
			return KeyHasher.GetHashFromKeyData(ScanCode, Extended);
		}

		public static bool operator !=(KBHookStruct key1, KBHookStruct key2)
		{
			return !(key1 == key2);
		}

	}

    public class KeyMapperKeyPressedEventArgs : EventArgs
	{
        private KBHookStruct _key;

		public KBHookStruct Key
		{
			get { return _key; }
		}

		// Constructor 
		public KeyMapperKeyPressedEventArgs(KBHookStruct key)
		{
            _key = key;
		}

	}
}

