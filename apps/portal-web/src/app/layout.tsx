import type { Metadata } from "next";
import type { ReactNode } from "react";
import "./globals.css";

export const metadata: Metadata = {
  title: "FlowOps Portal",
  description: "Submit requests and complete tasks in FlowOps Portal.",
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body className="surface-portal">{children}</body>
    </html>
  );
}
