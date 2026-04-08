import { Notification } from "electron";

const APP_NAME = "DevBrain";

export function showInfo(title: string, body: string): void {
  new Notification({ title: `${APP_NAME}: ${title}`, body }).show();
}

export function showError(title: string, body: string): void {
  new Notification({
    title: `${APP_NAME}: ${title}`,
    body,
    urgency: "critical",
  }).show();
}

export function showProgress(title: string, body: string): Notification {
  const n = new Notification({ title: `${APP_NAME}: ${title}`, body });
  n.show();
  return n;
}
