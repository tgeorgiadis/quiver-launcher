//! Artwork far bigger than it's ever shown, made smaller once. LEGO Island's
//! SteamGridDB logo is 18106 x 4509: WebKitGTK scaled it (and its drop shadow)
//! from full size each time Details drew it, and the page lagged.
//!
//! The page asks only for a picture it found oversized; the smaller copy (a PNG)
//! is kept in `art-cache/` in the data folder, named by the URL's hash.
use sha2::{Digest, Sha256};
use std::io::Cursor;
use std::path::Path;
use tauri::State;

use crate::Data;

/// The longest side a picture is made to fit.
const FIT: u32 = 2048;

/// The smaller copy of the picture at `url`, as PNG bytes.
#[tauri::command]
pub async fn tv_fit_art(data: State<'_, Data>, url: String) -> Result<tauri::ipc::Response, String> {
    if !url.starts_with("https://") {
        return Err("Only web pictures are made smaller.".into());
    }
    let cached = data.0.join("art-cache").join(format!("{}.png", hex::encode(Sha256::digest(url.as_bytes()))));
    if let Ok(bytes) = std::fs::read(&cached) {
        return Ok(tauri::ipc::Response::new(bytes));
    }
    let original = reqwest::Client::builder()
        .user_agent("QuiverLauncher/4")
        .build()
        .map_err(|e| e.to_string())?
        .get(&url)
        .send()
        .await
        .and_then(|r| r.error_for_status())
        .map_err(|e| e.to_string())?
        .bytes()
        .await
        .map_err(|e| e.to_string())?;
    let bytes = tauri::async_runtime::spawn_blocking(move || fit(&original, &cached)).await.map_err(|e| e.to_string())??;
    Ok(tauri::ipc::Response::new(bytes))
}

/// The picture fitted into FIT x FIT as PNG, also saved at `cache`.
fn fit(original: &[u8], cache: &Path) -> Result<Vec<u8>, String> {
    let image = image::load_from_memory(original).map_err(|e| e.to_string())?;
    let smaller = if image.width().max(image.height()) > FIT { image.resize(FIT, FIT, image::imageops::FilterType::Triangle) } else { image };
    let mut out = Vec::new();
    smaller.write_to(&mut Cursor::new(&mut out), image::ImageFormat::Png).map_err(|e| e.to_string())?;
    if let Some(dir) = cache.parent() {
        let _ = std::fs::create_dir_all(dir).and_then(|_| std::fs::write(cache, &out));
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_huge_picture_is_fitted_and_kept_and_its_shape_stays() {
        let mut huge = Vec::new();
        image::DynamicImage::new_rgba8(8000, 2000).write_to(&mut Cursor::new(&mut huge), image::ImageFormat::Png).unwrap();
        let dir = tempfile::tempdir().unwrap();
        let cache = dir.path().join("art-cache/x.png");
        let out = image::load_from_memory(&fit(&huge, &cache).unwrap()).unwrap();
        assert_eq!((out.width(), out.height()), (2048, 512));
        assert!(cache.is_file());
    }
}
