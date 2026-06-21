import { send } from '../../bridge/ipc';

export function requestModelsStatus() {
  send({ type: 'get_models_status' });
}

export function downloadModel(compositeName: string) {
  send({ type: 'download_model', model: compositeName });
}

export function deleteModel(compositeName: string) {
  send({ type: 'delete_model', model: compositeName });
}

export function loadModel(compositeName: string) {
  send({ type: 'load_model', model: compositeName });
}
