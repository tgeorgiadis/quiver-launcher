import { Component, type ErrorInfo, type ReactNode } from "react";
import { native } from "./native";
import { captureError } from "./telemetry";

type Props = {
  fallback: (error: Error, reset: () => void) => ReactNode;
  /** Shows the children again when this changes, such as on going to another page. */
  resetKey?: string;
  /** Told after the error is written down and reported. */
  onCaught?: () => void;
  children: ReactNode;
};

/** An error for quiver.log: WebKit's stack leaves out the message, which other browsers start it with. */
export function errorText(error: unknown) {
  if (!(error instanceof Error)) return String(error);
  const stack = error.stack ?? "";
  return stack.includes(error.message) ? stack : `${error.name}: ${error.message}\n${stack}`;
}

/**
 * Shows `fallback` where part of the window failed, and writes the error to
 * quiver.log (and error tracking, when usage data is on), instead of React
 * taking the whole window down with it.
 */
export class ErrorBoundary extends Component<Props, { error: Error | null }> {
  state = { error: null as Error | null };
  static getDerivedStateFromError(error: unknown) {
    return { error: error instanceof Error ? error : new Error(String(error)) };
  }
  componentDidCatch(error: unknown, info: ErrorInfo) {
    void native.logError(`ui: ${errorText(error)}${info.componentStack ?? ""}`).catch(() => {});
    captureError(error, { handled: "error_boundary" });
    this.props.onCaught?.();
  }
  componentDidUpdate(previous: Props) {
    if (this.state.error && previous.resetKey !== this.props.resetKey) this.reset();
  }
  reset = () => this.setState({ error: null });
  render() {
    return this.state.error ? this.props.fallback(this.state.error, this.reset) : this.props.children;
  }
}
