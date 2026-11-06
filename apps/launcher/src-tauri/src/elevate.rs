//! Programs Windows won't start without administrator rights: their manifest
//! asks for them, so starting one directly fails with `ELEVATION_REQUIRED`.
//! Only the shell can ask the player, with the same prompt a double click brings.
//!
//! What the player set in the environment doesn't reach the program: Windows
//! starts it from its own service, not from the launcher.

use std::ffi::{OsStr, OsString};
use std::os::windows::ffi::OsStrExt;
use std::path::{Path, PathBuf};
use windows_sys::Win32::Foundation::{CloseHandle, HANDLE, WAIT_OBJECT_0};
use windows_sys::Win32::System::Com::{CoInitializeEx, CoUninitialize, COINIT_APARTMENTTHREADED, COINIT_DISABLE_OLE1DDE};
use windows_sys::Win32::System::Threading::{WaitForSingleObject, INFINITE};
use windows_sys::Win32::UI::Shell::{ShellExecuteExW, SEE_MASK_FLAG_NO_UI, SEE_MASK_NOASYNC, SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW};
use windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;

/// `spawn`'s error for a program that needs administrator rights.
pub const REQUIRED: i32 = 740;
/// The error when the player says no to the prompt.
pub const DECLINED: i32 = 1223;

/// A program the shell started. Closes its handle when dropped.
pub struct Process(HANDLE);

// A process handle can be waited on from any thread.
unsafe impl Send for Process {}

impl Process {
    /// Waits for it to close; whether that could be told.
    pub fn wait(&self) -> bool {
        // SAFETY: the handle is open until `self` drops.
        unsafe { WaitForSingleObject(self.0, INFINITE) == WAIT_OBJECT_0 }
    }
}

impl Drop for Process {
    fn drop(&mut self) {
        // SAFETY: the shell handed it over, and only this closes it.
        unsafe { CloseHandle(self.0) };
    }
}

/// Starts a program with administrator rights, once the player allows it.
/// None when it's running but Windows gave no handle to wait on.
pub fn start(program: &Path, args: &[String], dir: &Path) -> std::io::Result<Option<Process>> {
    shell_start("runas", program, args, dir)
}

fn shell_start(verb: &str, program: &Path, args: &[String], dir: &Path) -> std::io::Result<Option<Process>> {
    let (verb, program, line, dir): (String, PathBuf, String, PathBuf) = (verb.to_owned(), program.to_owned(), command_line(args), dir.to_owned());
    // The shell wants COM ready on its thread, which isn't this one's to change; the prompt waits there as long as the player does.
    std::thread::spawn(move || {
        let (verb, program, line, dir) = (wide(verb.as_ref()), wide(program.as_os_str()), wide(OsString::from(line).as_os_str()), wide(dir.as_os_str()));
        // SAFETY: every pointer is to a terminated string alive for the call, and the struct is zeroed with its size set, as the API asks.
        unsafe {
            CoInitializeEx(std::ptr::null(), (COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE) as u32);
            let mut info: SHELLEXECUTEINFOW = std::mem::zeroed();
            info.cbSize = std::mem::size_of::<SHELLEXECUTEINFOW>() as u32;
            info.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI | SEE_MASK_NOASYNC;
            info.lpVerb = verb.as_ptr();
            info.lpFile = program.as_ptr();
            info.lpParameters = line.as_ptr();
            info.lpDirectory = dir.as_ptr();
            info.nShow = SW_SHOWNORMAL;
            let started = ShellExecuteExW(&mut info) != 0;
            let result = if started { Ok((!info.hProcess.is_null()).then_some(Process(info.hProcess))) } else { Err(std::io::Error::last_os_error()) };
            CoUninitialize();
            result
        }
    })
    .join()
    .unwrap_or_else(|_| Err(std::io::ErrorKind::Other.into()))
}

fn wide(text: &OsStr) -> Vec<u16> {
    text.encode_wide().chain(std::iter::once(0)).collect()
}

/// The arguments as one line, which programs split back up by the C runtime's rules.
fn command_line(args: &[String]) -> String {
    args.iter().map(|arg| quoted(arg)).collect::<Vec<_>>().join(" ")
}

fn quoted(arg: &str) -> String {
    if !arg.is_empty() && !arg.contains([' ', '\t', '"']) {
        return arg.to_owned();
    }
    let mut line = String::from('"');
    let mut slashes = 0;
    for c in arg.chars() {
        match c {
            '\\' => slashes += 1,
            // Backslashes count only before a quote, which they'd escape.
            '"' => {
                line.push_str(&"\\".repeat(slashes * 2 + 1));
                slashes = 0;
            }
            _ => {
                line.push_str(&"\\".repeat(slashes));
                slashes = 0;
            }
        }
        if c != '\\' {
            line.push(c);
        }
    }
    // As many again before the closing quote, so it still closes.
    line.push_str(&"\\".repeat(slashes * 2));
    line.push('"');
    line
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn arguments_come_back_as_they_were_given() {
        assert_eq!(command_line(&["-w".into(), "two words".into(), "".into()]), r#"-w "two words" """#);
        assert_eq!(quoted(r"C:\Games\"), r"C:\Games\", "nothing to quote");
        assert_eq!(quoted(r"C:\My Games\"), r#""C:\My Games\\""#, "the last backslash doesn't escape the closing quote");
        assert_eq!(quoted(r#"say "hi""#), r#""say \"hi\"""#);
        assert_eq!(quoted(r#"a\"b c"#), r#""a\\\"b c""#);
        assert_eq!(quoted(r"a\b c"), r#""a\b c""#, "backslashes elsewhere stay single");
    }

    #[test]
    fn the_shell_starts_a_program_in_its_folder_and_tells_when_it_closes() {
        // The same call without "runas", which would ask whoever runs the tests.
        let dir = tempfile::tempdir().unwrap();
        let cmd = std::env::var_os("ComSpec").map(PathBuf::from).unwrap();
        let args = ["/c".to_string(), "cd".to_string(), ">".to_string(), "where it ran.txt".to_string()];
        let process = shell_start("open", &cmd, &args, dir.path()).unwrap().expect("a handle to wait on");
        assert!(process.wait());
        let ran = std::fs::read_to_string(dir.path().join("where it ran.txt")).unwrap();
        assert_eq!(std::fs::canonicalize(ran.trim()).unwrap(), std::fs::canonicalize(dir.path()).unwrap());
    }

    #[test]
    fn a_program_that_isnt_there_is_an_error() {
        let dir = tempfile::tempdir().unwrap();
        assert!(shell_start("open", &dir.path().join("none.exe"), &[], dir.path()).is_err());
    }
}
