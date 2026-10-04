import { Component, type ErrorInfo, type ReactNode } from "react";
import { native } from "./native";

type Props = {
  fallback: (error: Error, reset: () => void) => ReactNode;
  /** Shows the children again when this changes, such as on going to another page. */
  resetKey?: string;
  children: ReactNode;
};

/**
 * Shows `fallback` where part of the window failed, and writes the error to
 * quiver.log, instead of React taking the whole window down with it.
 */
export class ErrorBoundary extends Component<Props, { error: Error | null }> {
  state = { error: null as Error | null };
  static getDerivedStateFromError(error: unknown) {
    return { error: error instanceof Error ? error : new Error(String(error)) };
  }
  componentDidCatch(error: unknown, info: ErrorInfo) {
    const text = error instanceof Error ? (error.stack ?? error.message) : String(error);
    void native.logError(`ui: ${text}${info.componentStack ?? ""}`).catch(() => {});
  }
  componentDidUpdate(previous: Props) {
    if (this.state.error && previous.resetKey !== this.props.resetKey) this.reset();
  }
  reset = () => this.setState({ error: null });
  render() {
    return this.state.error ? this.props.fallback(this.state.error, this.reset) : this.props.children;
  }
}
