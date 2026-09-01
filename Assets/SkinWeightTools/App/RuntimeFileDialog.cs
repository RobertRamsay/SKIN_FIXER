using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SkinWeightTools.App
{
    /// <summary>
    /// Blocking "open file" dialog available in a player build. Unity ships no runtime file
    /// picker, so standalone Windows calls comdlg32 directly and the editor borrows
    /// EditorUtility. Any other platform reports that it has no picker rather than
    /// silently doing nothing.
    /// </summary>
    public static class RuntimeFileDialog
    {
        public const string ModelFilter = "Model files\0*.glb;*.gltf\0glTF Binary (*.glb)\0*.glb\0glTF (*.gltf)\0*.gltf\0All files\0*.*\0\0";

        /// <summary>Returns an empty string when the user cancels or the platform has no picker.</summary>
        public static string OpenFile(string title, string filter)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.OpenFilePanel(title, string.Empty, "glb,gltf");
#elif UNITY_STANDALONE_WIN
            return OpenFileWindows(title, filter);
#else
            Debug.LogError("RuntimeFileDialog: no native file picker on this platform. Pass a path to SkinFixerRuntimeApp.LoadModel instead.");
            return string.Empty;
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const int MaxPathLength = 4096;

        private const int OfnNoChangeDir = 0x00000008;
        private const int OfnPathMustExist = 0x00000800;
        private const int OfnFileMustExist = 0x00001000;
        private const int OfnExplorer = 0x00080000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int structSize;
            public IntPtr owner;
            public IntPtr instance;
            [MarshalAs(UnmanagedType.LPWStr)] public string filter;
            public IntPtr customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            [MarshalAs(UnmanagedType.LPWStr)] public string initialDirectory;
            [MarshalAs(UnmanagedType.LPWStr)] public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            [MarshalAs(UnmanagedType.LPWStr)] public string defaultExtension;
            public IntPtr customData;
            public IntPtr hook;
            public IntPtr templateName;
            public IntPtr reservedPointer;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetOpenFileNameW")]
        private static extern bool GetOpenFileName(ref OpenFileName options);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private static string OpenFileWindows(string title, string filter)
        {
            // The dialog writes the chosen path back into this buffer, so it cannot be a
            // marshalled string field. Unicode means two bytes per character.
            IntPtr buffer = Marshal.AllocHGlobal(MaxPathLength * 2);

            try
            {
                for (int index = 0; index < 4; index++)
                {
                    Marshal.WriteByte(buffer, index, 0);
                }

                OpenFileName options = new OpenFileName();
                options.structSize = Marshal.SizeOf(typeof(OpenFileName));
                options.owner = GetActiveWindow();
                options.filter = filter;
                options.filterIndex = 1;
                options.file = buffer;
                options.maxFile = MaxPathLength;
                options.title = title;
                options.defaultExtension = "glb";

                // NOCHANGEDIR matters: without it the dialog leaves the process working
                // directory wherever the user last browsed, which breaks later relative paths.
                options.flags = OfnExplorer | OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir;

                bool picked = GetOpenFileName(ref options);

                if (picked == false)
                {
                    return string.Empty;
                }

                return Marshal.PtrToStringUni(buffer);
            }
            catch (Exception error)
            {
                Debug.LogError("RuntimeFileDialog: native dialog failed. " + error.Message);
                return string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
#endif
    }
}
