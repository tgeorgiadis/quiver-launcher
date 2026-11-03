//! The player's own pictures for an app (custom-art.tsx): a copy of each file
//! they pick, sized for where it shows, in the data folder. The page loads them
//! through the `quiverart` protocol, so a library's worth isn't kept in memory,
//! and a shortcut reads them from there too.
use image::{DynamicImage, GenericImageView};
use std::collections::HashSet;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};

pub const PROTOCOL: &str = "quiverart";
const FOLDER: &str = "custom-art";
/// The kinds of picture that can be picked: what the `image` crate here reads.
pub const EXTENSIONS: &[&str] = &["png", "jpg", "jpeg", "gif", "webp"];
/// A very large photo: past it, it's not a file to read into memory.
const MAX_BYTES: u64 = 100 * 1024 * 1024;

pub fn folder(data: &Path) -> PathBuf {
    data.join(FOLDER)
}

/// The longest side a slot's picture is kept at: about twice what it shows at, as a window can be large and a card's
/// picture decoded whole for each of a library's cards makes scrolling stutter.
fn longest_side(slot: &str) -> Result<u32, String> {
    match slot {
        "box" => Ok(1800),
        "cover" => Ok(1840),
        "banner" => Ok(3840),
        "logo" => Ok(1920),
        "icon" => Ok(512),
        _ => Err("That isn't a kind of artwork.".into()),
    }
}

/// A name this module made: nothing that could lead out of the folder.
fn plain(name: &str) -> bool {
    !name.is_empty() && !name.starts_with('.') && name.chars().all(|c| c.is_ascii_alphanumeric() || c == '-' || c == '.')
}

/// Keeps a copy of the picture at `from`, for `slot`; returns its name. Transparency stays in a PNG; anything else
/// is a JPEG, far smaller for a photo.
pub fn import(from: &Path, data: &Path, slot: &str) -> Result<String, String> {
    let longest = longest_side(slot)?;
    let kind = from.extension().and_then(|e| e.to_str()).map(str::to_ascii_lowercase);
    if !kind.is_some_and(|k| EXTENSIONS.contains(&k.as_str())) {
        return Err("That isn't a picture Quiver can show.".into());
    }
    if std::fs::metadata(from).map_err(|e| e.to_string())?.len() > MAX_BYTES {
        return Err("That picture is too large to use.".into());
    }
    let bytes = std::fs::read(from).map_err(|e| e.to_string())?;
    let image = image::load_from_memory(&bytes).map_err(|_| "That file can't be read as a picture.".to_string())?;
    let (width, height) = image.dimensions();
    let image = if width.max(height) > longest { image.resize(longest, longest, image::imageops::FilterType::Lanczos3) } else { image };
    let dir = folder(data);
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    // A new name each time: the page keeps what it has loaded under a name.
    static COUNT: AtomicU64 = AtomicU64::new(0);
    let made = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_or(0, |d| d.as_millis());
    let stem = format!("{slot}-{made}-{}", COUNT.fetch_add(1, Ordering::Relaxed));
    let name = if image.color().has_alpha() {
        let name = format!("{stem}.png");
        image.save(dir.join(&name)).map_err(|e| e.to_string())?;
        name
    } else {
        let name = format!("{stem}.jpg");
        DynamicImage::ImageRgb8(image.to_rgb8()).save(dir.join(&name)).map_err(|e| e.to_string())?;
        name
    };
    Ok(name)
}

/// Deletes the pictures other than `keep`: those replaced, or whose app was removed.
pub fn keep_only(data: &Path, keep: &[String]) {
    let keep: HashSet<&str> = keep.iter().map(String::as_str).collect();
    for file in std::fs::read_dir(folder(data)).into_iter().flatten().flatten() {
        if !file.file_name().to_str().is_some_and(|name| keep.contains(name)) {
            let _ = std::fs::remove_file(file.path());
        }
    }
}

/// The kept picture a `quiverart` address's path names, if it's one.
fn file_at(data: &Path, path: &str) -> Option<PathBuf> {
    let name = path.trim_start_matches('/');
    plain(name).then(|| folder(data).join(name))
}

/// The page's request for a kept picture. They never change under a name, so it needn't ask twice.
pub fn respond(data: &Path, path: &str) -> tauri::http::Response<Vec<u8>> {
    let file = file_at(data, path);
    let bytes = file.as_ref().and_then(|file| std::fs::read(file).ok());
    let builder = tauri::http::Response::builder();
    match (file, bytes) {
        (Some(file), Some(bytes)) => builder
            .header("Content-Type", if file.extension().is_some_and(|e| e == "png") { "image/png" } else { "image/jpeg" })
            .header("Cache-Control", "max-age=31536000, immutable")
            .body(bytes),
        _ => builder.status(404).body(Vec::new()),
    }
    .expect("headers this module wrote are valid")
}

/// The kept picture a `quiverart` address means, as Windows and Android (`http://quiverart.localhost/…`) and others
/// (`quiverart://localhost/…`) write them.
pub fn file_of_address(data: &Path, address: &str) -> Option<PathBuf> {
    let path = address.strip_prefix("http://quiverart.localhost/").or_else(|| address.strip_prefix("quiverart://localhost/"))?;
    file_at(data, path)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn picture(dir: &Path, name: &str, width: u32, height: u32, alpha: bool) -> PathBuf {
        let path = dir.join(name);
        if alpha {
            DynamicImage::new_rgba8(width, height).save(&path).unwrap();
        } else {
            DynamicImage::new_rgb8(width, height).save(&path).unwrap();
        }
        path
    }

    #[test]
    fn a_large_picture_is_kept_smaller_and_a_transparent_one_stays_a_png() {
        let root = tempfile::tempdir().unwrap();
        let from = picture(root.path(), "big.png", 1024, 512, false);
        let name = import(&from, root.path(), "icon").unwrap();
        assert!(name.starts_with("icon-") && name.ends_with(".jpg"), "{name}");
        assert_eq!(image::open(folder(root.path()).join(&name)).unwrap().dimensions(), (512, 256));
        let from = picture(root.path(), "logo.png", 64, 32, true);
        let name = import(&from, root.path(), "logo").unwrap();
        assert!(name.ends_with(".png"), "{name}");
        assert_eq!(image::open(folder(root.path()).join(&name)).unwrap().dimensions(), (64, 32));
    }

    #[test]
    fn only_pictures_of_a_known_slot_are_kept() {
        let root = tempfile::tempdir().unwrap();
        let from = picture(root.path(), "a.png", 8, 8, false);
        assert!(import(&from, root.path(), "wallpaper").is_err());
        let text = root.path().join("a.txt");
        std::fs::write(&text, "not a picture").unwrap();
        assert!(import(&text, root.path(), "box").is_err());
        let fake = root.path().join("fake.png");
        std::fs::write(&fake, "not a picture").unwrap();
        assert!(import(&fake, root.path(), "box").is_err());
    }

    #[test]
    fn keeps_only_the_pictures_named() {
        let root = tempfile::tempdir().unwrap();
        let dir = folder(root.path());
        std::fs::create_dir_all(&dir).unwrap();
        for name in ["keep.png", "old.png"] {
            std::fs::write(dir.join(name), "x").unwrap();
        }
        keep_only(root.path(), &["keep.png".into()]);
        assert!(dir.join("keep.png").exists() && !dir.join("old.png").exists());
    }

    #[test]
    fn an_address_never_leads_out_of_the_folder() {
        let root = tempfile::tempdir().unwrap();
        let dir = folder(root.path());
        assert_eq!(file_of_address(root.path(), "http://quiverart.localhost/a-1.png"), Some(dir.join("a-1.png")));
        assert_eq!(file_of_address(root.path(), "quiverart://localhost/a-1.png"), Some(dir.join("a-1.png")));
        assert_eq!(file_of_address(root.path(), "https://example.com/a.png"), None);
        for bad in ["../secret.png", "..%2Fsecret.png", ".hidden", "a/b.png", "a\\b.png", ""] {
            assert_eq!(file_at(root.path(), bad), None, "{bad}");
        }
        assert_eq!(respond(root.path(), "/missing.png").status(), 404);
    }
}
