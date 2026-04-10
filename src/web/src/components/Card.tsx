import type { PropsWithChildren } from "react";

interface CardProps extends PropsWithChildren {
  title: string;
  subtitle?: string;
}

export function Card({ title, subtitle, children }: CardProps) {
  return (
    <section className="card">
      <div className="card-header">
        <div>
          <h2>{title}</h2>
          {subtitle ? <p>{subtitle}</p> : null}
        </div>
      </div>
      {children}
    </section>
  );
}
