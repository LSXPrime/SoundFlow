// Import the factory function from the Emscripten-generated glue code.
import miniaudioFactory from './library.js';

console.log("Loading custom miniaudio library...");

const miniaudioModule = await miniaudioFactory();

console.log("Miniaudio library loaded. Exposing functions to C#...");

// Expose Wasm Functions for C# JSImport

const functionsToExpose = [
    // Your custom functions
    'sf_free', 'sf_get_devices', 'sf_allocate_encoder', 'sf_allocate_decoder',
    'sf_allocate_context', 'sf_allocate_device', 'sf_allocate_decoder_config',
    'sf_allocate_encoder_config', 'sf_allocate_device_config',

    // Native Miniaudio functions
    'ma_encoder_init_file', 'ma_encoder_uninit', 'ma_encoder_write_pcm_frames',
    'ma_decoder_init', 'ma_decoder_uninit', 'ma_decoder_read_pcm_frames',
    'ma_decoder_seek_to_pcm_frame', 'ma_decoder_get_length_in_pcm_frames',
    'ma_context_init', 'ma_context_uninit', 'ma_device_init', 'ma_device_uninit',
    'ma_device_start', 'ma_device_stop',

    // Standard memory functions
    'malloc', 'free'
];

functionsToExpose.forEach(name => {
    const wasmName = '_' + name;
    if (miniaudioModule[wasmName]) {
        // Attach to window, removing the leading underscore from the name
        window[name] = miniaudioModule[wasmName];
    } else {
        console.warn(`Attempted to expose function "${name}" but it was not found in the Wasm module.`);
    }
});

window.addFunction = miniaudioModule.addFunction;
window.removeFunction = miniaudioModule.removeFunction;


console.log("All custom functions exposed globally.");
console.log("Starting WebAssembly application...");